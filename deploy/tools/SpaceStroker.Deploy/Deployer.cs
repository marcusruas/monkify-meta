using System.Text;
using System.Text.Json;

namespace SpaceStroker.Deploy;

/// <summary>
/// Implements the deploy pipeline: sync secrets → Secret Manager, emit Terraform vars,
/// build/push images, apply Terraform, publish the static sites, and teardown.
/// Every external step shells out (gcloud/docker/terraform/gsutil/yarn/npm); nothing here
/// prints or persists a secret value.
/// </summary>
public sealed class Deployer(SecretsFile secrets, RepoPaths paths)
{
    private readonly SecretsFile _s = secrets;
    private readonly RepoPaths _paths = paths;

    // --- Public commands ---------------------------------------------------------

    public void DeployAll()
    {
        SyncSecrets();
        EmitTfvars();
        TerraformApply(_paths.TerraformWorkload);
        // Consumers before API (it owns EF migrations/seeding).
        DeployComponent("consumers");
        DeployComponent("api");
        DeployComponent("front");
        DeployComponent("lp");
        Console.WriteLine("Deploy complete.");
    }

    public void DeployComponent(string component)
    {
        switch (component)
        {
            case "api":
                BuildAndPushImage("api", Path.Combine("SpaceStroker.Api", "Dockerfile"));
                UpdateCloudRun("spacestroker-api");
                break;
            case "consumers":
                BuildAndPushImage("consumers", Path.Combine("SpaceStroker.Consumers", "Dockerfile"));
                UpdateCloudRun("spacestroker-consumers");
                break;
            case "front":
                BuildStaticSite(_paths.FrontRepo, isYarn: true);
                UploadAndInvalidate(BucketName("front"), Path.Combine(_paths.FrontRepo, "dist"));
                break;
            case "lp":
                BuildStaticSite(_paths.LpRepo, isYarn: false);
                UploadAndInvalidate(BucketName("lp"), Path.Combine(_paths.LpRepo, "dist"));
                break;
            default:
                throw new DeployException($"Unknown component '{component}'. Use api|consumers|front|lp.");
        }
    }

    public void Teardown()
    {
        Console.WriteLine();
        Console.WriteLine("!! TEARDOWN — this DESTROYS the workload layer, including Cloud SQL and ALL its data.");
        Console.WriteLine("!! The persistent layer (DNS zone + domain) is preserved.");
        Console.Write($"Type the environment name ('{_s.Environment}') to confirm: ");
        var typed = Console.ReadLine();
        if (!string.Equals(typed?.Trim(), _s.Environment, StringComparison.Ordinal))
        {
            Console.WriteLine("Confirmation did not match. Aborting — nothing was destroyed.");
            return;
        }

        EmitTfvars(deletionProtectionOverride: false);
        var wl = _paths.TerraformWorkload;
        TerraformInit(wl);
        // Deletion-protection is already false in the emitted tfvars; apply that first so destroy can proceed.
        ProcessRunner.RunOrThrow("terraform", $"apply -auto-approve -var-file=\"{AutoTfvarsPath(wl)}\"", wl);
        ProcessRunner.RunOrThrow("terraform", $"destroy -auto-approve -var-file=\"{AutoTfvarsPath(wl)}\"", wl);
        Console.WriteLine("Workload destroyed. Domain/DNS preserved. Re-run `deploy` to recreate + re-attach.");
    }

    // --- Steps -------------------------------------------------------------------

    /// <summary>Push each secret value as a new Secret Manager version (create the secret if absent).</summary>
    public void SyncSecrets()
    {
        Console.WriteLine($"Syncing {_s.Secrets.Count} secret(s) to Secret Manager (project {_s.Gcp.ProjectId})…");
        foreach (var (key, value) in _s.Secrets)
        {
            var secretId = Sanitize(key);
            // Create the secret if it does not exist (ignore "already exists").
            ProcessRunner.Run("gcloud",
                $"secrets create {secretId} --project={_s.Gcp.ProjectId} --replication-policy=automatic");

            // Add the value as a new version via stdin (never echoed / never on the command line).
            AddSecretVersionViaStdin(secretId, value);
            Console.WriteLine($"  synced {secretId}");
        }
    }

    private void AddSecretVersionViaStdin(string secretId, string value)
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, value);
            ProcessRunner.RunOrThrow("gcloud",
                $"secrets versions add {secretId} --project={_s.Gcp.ProjectId} --data-file=\"{tmp}\"");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    /// <summary>Write the non-secret Terraform variables to production.auto.tfvars.json (git-ignored).</summary>
    public void EmitTfvars(bool? deletionProtectionOverride = null)
    {
        var wl = _paths.TerraformWorkload;
        var region = _s.Gcp.Region;
        var project = _s.Gcp.ProjectId;
        var registry = $"{region}-docker.pkg.dev/{project}/spacestroker";

        var plainEnv = new Dictionary<string, string>();
        foreach (var (k, v) in _s.AppConfig)
            plainEnv[k] = v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString();

        var vars = new Dictionary<string, object?>
        {
            ["project_id"] = project,
            ["region"] = region,
            ["api_image"] = $"{registry}/api:latest",
            ["consumers_image"] = $"{registry}/consumers:latest",
            ["api_min_instances"] = _s.Gcp.ApiMinInstances,
            ["api_max_instances"] = _s.Gcp.ApiMaxInstances,
            ["consumer_count"] = _s.Gcp.ConsumerCount,
            ["enable_deletion_protection"] = deletionProtectionOverride ?? _s.Gcp.EnableDeletionProtection,
            ["db_app_password"] = _s.Secrets.TryGetValue("ConnectionStrings:SpaceStroker", out var cs)
                ? ExtractPassword(cs)
                : "",
            ["front_bucket_name"] = BucketName("front"),
            ["lp_bucket_name"] = BucketName("lp"),
            ["dns_zone_name"] = _s.Gcp.DnsZoneName,
            ["domain_api"] = _s.Gcp.Domains.Api,
            ["domain_app"] = _s.Gcp.Domains.App,
            ["domain_lp"] = _s.Gcp.Domains.Lp,
            ["domain_lp_www"] = _s.Gcp.Domains.LpWww,
            ["plain_env"] = plainEnv,
        };

        var path = AutoTfvarsPath(wl);
        File.WriteAllText(path, JsonSerializer.Serialize(vars, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Wrote Terraform vars → {Path.GetFileName(path)} (git-ignored).");
    }

    private void TerraformInit(string dir)
    {
        ProcessRunner.RunOrThrow("terraform",
            $"init -backend-config=\"bucket={_s.Gcp.StateBucket}\" -backend-config=\"prefix=workload\"", dir);
    }

    private void TerraformApply(string dir)
    {
        TerraformInit(dir);
        ProcessRunner.RunOrThrow("terraform", $"apply -auto-approve -var-file=\"{AutoTfvarsPath(dir)}\"", dir);
    }

    private void BuildAndPushImage(string image, string dockerfileRelative)
    {
        var tag = $"{_s.Gcp.Region}-docker.pkg.dev/{_s.Gcp.ProjectId}/spacestroker/{image}:latest";
        var context = Path.Combine(_paths.BackendRepo, "src");
        ProcessRunner.RunOrThrow("docker",
            $"build -t {tag} -f \"{Path.Combine(context, dockerfileRelative)}\" \"{context}\"");
        ProcessRunner.RunOrThrow("docker", $"push {tag}");
    }

    private void UpdateCloudRun(string service)
    {
        var tag = service.EndsWith("api")
            ? $"{_s.Gcp.Region}-docker.pkg.dev/{_s.Gcp.ProjectId}/spacestroker/api:latest"
            : $"{_s.Gcp.Region}-docker.pkg.dev/{_s.Gcp.ProjectId}/spacestroker/consumers:latest";
        ProcessRunner.RunOrThrow("gcloud",
            $"run services update {service} --project={_s.Gcp.ProjectId} --region={_s.Gcp.Region} --image={tag}");
    }

    private void BuildStaticSite(string repo, bool isYarn)
    {
        WriteFrontEnv(repo);
        if (isYarn)
        {
            ProcessRunner.RunOrThrow("yarn", "install --frozen-lockfile", repo);
            ProcessRunner.RunOrThrow("yarn", "build", repo);
        }
        else
        {
            ProcessRunner.RunOrThrow("npm", "ci", repo);
            ProcessRunner.RunOrThrow("npm", "run build", repo);
        }
    }

    private void WriteFrontEnv(string repo)
    {
        if (_s.FrontBuild.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var (k, v) in _s.FrontBuild)
            sb.AppendLine($"{k}={v}");
        // Vue CLI's highest-priority env file; git-ignored in the front repo.
        File.WriteAllText(Path.Combine(repo, ".env.production.local"), sb.ToString());
    }

    private void UploadAndInvalidate(string bucket, string distDir)
    {
        if (!Directory.Exists(distDir))
            throw new DeployException($"Build output not found: {distDir}. Did the build succeed?");
        ProcessRunner.RunOrThrow("gsutil", $"-m rsync -r -d \"{distDir}\" gs://{bucket}");
        // CDN cache invalidation (best-effort; url-map name follows the module convention).
        ProcessRunner.Run("gcloud",
            $"compute url-maps invalidate-cdn-cache spacestroker-urlmap --project={_s.Gcp.ProjectId} --path=\"/*\" --async");
    }

    // --- Helpers -----------------------------------------------------------------

    private string BucketName(string site) => $"spacestroker-{site}-{_s.Gcp.ProjectId}";

    private static string AutoTfvarsPath(string dir) => Path.Combine(dir, "production.auto.tfvars.json");

    private static string Sanitize(string configKey) => configKey.Replace(':', '-');

    private static string ExtractPassword(string connectionString)
    {
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Trim().Equals("Password", StringComparison.OrdinalIgnoreCase))
                return kv[1].Trim();
        }
        return "";
    }
}
