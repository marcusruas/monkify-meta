using System.Text;
using System.Text.Json;

namespace SpaceStroker.Deploy;

/// <summary>
/// Implements the deploy pipeline: sync secrets → Secret Manager, emit Terraform vars,
/// build/push images, apply Terraform, publish the static sites, and teardown.
/// Every external step shells out (gcloud/docker/terraform/gsutil/yarn/npm); nothing here
/// prints or persists a secret value.
///
/// Everything project-global is named through <see cref="EnvNaming"/>, because development and
/// production share one GCP project.
/// </summary>
public sealed class Deployer(SecretsFile secrets, RepoPaths paths)
{
    private readonly SecretsFile _s = secrets;
    private readonly RepoPaths _paths = paths;
    private readonly EnvNaming _naming = EnvNaming.From(secrets);

    // --- Public commands ---------------------------------------------------------

    public void DeployAll()
    {
        PreFlight();
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
                UpdateCloudRun(_naming.ApiService, "api");
                break;
            case "consumers":
                BuildAndPushImage("consumers", Path.Combine("SpaceStroker.Consumers", "Dockerfile"));
                UpdateCloudRun(_naming.ConsumersService, "consumers");
                break;
            case "front":
                BuildStaticSite(_paths.FrontRepo, isYarn: true);
                UploadAndInvalidate(StaticSite.Front, _naming.FrontBucket, Path.Combine(_paths.FrontRepo, "dist"));
                break;
            case "lp":
                BuildStaticSite(_paths.LpRepo, isYarn: false);
                UploadAndInvalidate(StaticSite.Lp, _naming.LpBucket, Path.Combine(_paths.LpRepo, "dist"));
                break;
            default:
                throw new DeployException($"Unknown component '{component}'. Use api|consumers|front|lp.");
        }
    }

    /// <summary>Validates naming and guarantees the persistent layer exists. Runs before every deploy.</summary>
    public void PreFlight()
    {
        _naming.Validate();
        ValidateSecretBindings();
        WarnOnZoneNameDrift();
        EnsurePersistentLayer();
    }

    /// <summary>
    /// Refuses to deploy while a `secrets` entry has no Terraform binding — see
    /// <see cref="SecretBindingCheck"/> for why that is invisible rather than loud.
    ///
    /// Runs in PreFlight, so it gates every deploy and not just an explicit `validate`: the whole
    /// point is to fail BEFORE SyncSecrets pushes a value nothing will ever mount.
    /// </summary>
    private void ValidateSecretBindings()
    {
        var mainTf = Path.Combine(_paths.TerraformWorkload, "main.tf");

        // Not a silent skip: the guard is only meaningful against the real stack, and a missing
        // main.tf means something far more wrong than an unbound secret.
        if (!File.Exists(mainTf))
            throw new DeployException($"Cannot verify secret bindings — {mainTf} does not exist.");

        var unbound = SecretBindingCheck.FindUnbound(_s.Secrets.Keys, File.ReadAllText(mainTf));

        if (unbound.Count == 0)
            return;

        throw new DeployException(
            $"{unbound.Count} secret(s) in secrets.{_s.Environment}.json are not wired into Cloud Run:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, unbound.Select(u => $"  - {u}")) +
            Environment.NewLine + Environment.NewLine +
            "Add each id to BOTH base_secret_ids and base_secret_env in " +
            "deploy/terraform/workload/main.tf, then re-run. Without them the value syncs to Secret " +
            "Manager and is never mounted, so the app silently uses its committed default.");
    }

    private void WarnOnZoneNameDrift()
    {
        var warning = _naming.DnsZoneNameWarning(_s.Gcp.DnsZoneName);
        if (warning is not null)
            Console.WriteLine($"!! {warning}");
    }

    /// <summary>Prints every derived id with PASS/FAIL and throws on the first cap violation.</summary>
    public void ValidateNaming()
    {
        Console.WriteLine($"Environment '{_s.Environment}' → env code '{_naming.EnvCode}', prefix '{_naming.NamePrefix}'");
        Console.WriteLine($"Project {_s.Gcp.ProjectId} ({_s.Gcp.Region})");
        Console.WriteLine();
        foreach (var (kind, id, limit, ok) in _naming.DerivedIds())
            Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {kind,-18} {id} ({id.Length}/{limit})");
        Console.WriteLine();
        _naming.Validate();
        ValidateSecretBindings();
        WarnOnZoneNameDrift();
        Console.WriteLine("All derived ids are within their GCP limits.");
        Console.WriteLine($"All {_s.Secrets.Count} secret(s) are bound to Cloud Run by terraform/workload/main.tf.");
    }

    /// <summary>
    /// Every environment tears down the WORKLOAD layer only by default; the persistent layer (DNS
    /// zone + state bucket) is preserved so the domain delegation and Terraform state survive a
    /// park-and-redeploy — the persistent layer costs almost nothing to keep. Pass
    /// <paramref name="purge"/> (CLI `--purge`, non-production only) to ALSO destroy the persistent
    /// layer and sweep secrets — a full, irreversible wipe.
    /// </summary>
    public void Teardown(bool purge = false)
    {
        if (!purge)
        {
            TeardownWorkloadOnly();
            return;
        }

        if (IsProduction)
            throw new DeployException(
                "--purge is blocked for production: its DNS zone + state bucket are permanent. " +
                "Plain `teardown` removes the production workload only.");
        TeardownTotal();
    }

    // --- Steps -------------------------------------------------------------------

    /// <summary>
    /// Bring each secret's CURRENT value into Secret Manager, then trim its history to
    /// <see cref="SecretVersionPruning.VersionsToKeep"/> versions.
    ///
    /// This used to push a new version unconditionally on every run, which is what produced a
    /// R$118.77 half-month bill from 24 secrets: Secret Manager bills per enabled version, and 692 of
    /// them had piled up, nearly all byte-identical duplicates. A value that has not changed is now
    /// left alone, and anything past the retention window is destroyed — see
    /// <see cref="SecretVersionPruning"/> for the full arithmetic.
    /// </summary>
    public void SyncSecrets()
    {
        Console.WriteLine($"Syncing {_s.Secrets.Count} secret(s) to Secret Manager (project {_s.Gcp.ProjectId})…");

        int pushed = 0, unchanged = 0, destroyed = 0;

        foreach (var (key, value) in _s.Secrets)
        {
            var secretId = _naming.SecretId(key);
            // Create the secret if it does not exist (ignore "already exists").
            ProcessRunner.Run("gcloud",
                $"secrets create {secretId} --project={_s.Gcp.ProjectId} --replication-policy=automatic");

            if (SecretVersionPruning.IsAlreadyStored(value, ReadLatestPayload(secretId)))
            {
                unchanged++;
                Console.WriteLine($"  unchanged {secretId}");
            }
            else
            {
                // Add the value as a new version via a temp file (never echoed / never on the command line).
                AddSecretVersionViaStdin(secretId, value);
                pushed++;
                Console.WriteLine($"  synced {secretId}");
            }

            destroyed += PruneVersionsOf(secretId);
        }

        Console.WriteLine(
            $"  {pushed} new version(s), {unchanged} unchanged, {destroyed} old version(s) destroyed.");
    }

    /// <summary>
    /// Destroy every version outside the retention window on every secret of this environment —
    /// the standalone cleanup behind `spacestroker-deploy prune-secrets`.
    ///
    /// Reads the live list from GCP rather than secrets.&lt;env&gt;.json on purpose: a key that drifted
    /// out of the file still bills every month, and those orphans are exactly what this is for.
    /// DESTROYING A VERSION IS IRREVERSIBLE — the payload cannot be recovered afterwards.
    /// </summary>
    public void PruneSecrets()
    {
        Console.WriteLine(
            $"Pruning {_naming.NamePrefix}-* secrets to the newest {SecretVersionPruning.VersionsToKeep} " +
            $"version(s) (project {_s.Gcp.ProjectId})…");

        var ids = ListEnvironmentSecretIds();
        if (ids.Count == 0)
        {
            Console.WriteLine("  no secrets found for this environment — nothing to prune.");
            return;
        }

        var destroyed = ids.Sum(id => PruneVersionsOf(id));

        Console.WriteLine($"  {destroyed} version(s) destroyed across {ids.Count} secret(s).");
    }

    /// <summary>The env-prefixed secret ids that actually exist in the project, oldest name first.</summary>
    private List<string> ListEnvironmentSecretIds()
    {
        var (code, output) = ProcessRunner.Capture("gcloud",
            $"secrets list --project={_s.Gcp.ProjectId} --format=\"value(name)\" --filter=\"name~{_naming.NamePrefix}-\"");

        if (code != 0)
            throw new DeployException(
                $"Could not list Secret Manager secrets for {_naming.NamePrefix}-* (gcloud exited {code}).");

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().Split('/').Last())
            .Where(id => id.StartsWith(_naming.NamePrefix + "-", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Destroy this secret's versions outside the retention window and return how many went.
    ///
    /// Deliberately non-fatal: pruning is housekeeping that runs inside a deploy, and a single
    /// permission error or API hiccup here must not abort the release it is attached to. A failure
    /// costs storage, never availability.
    /// </summary>
    private int PruneVersionsOf(string secretId, int keep = SecretVersionPruning.VersionsToKeep)
    {
        var (code, output) = ProcessRunner.Capture("gcloud",
            $"secrets versions list {secretId} --project={_s.Gcp.ProjectId} --format=\"csv[no-heading](name,state)\"");

        if (code != 0)
        {
            Console.WriteLine($"  ! could not list versions of {secretId} (gcloud exited {code}) — nothing pruned.");
            return 0;
        }

        var doomed = SecretVersionPruning.ToDestroy(SecretVersionPruning.Parse(output), keep);
        var destroyed = 0;

        foreach (var number in doomed)
        {
            var exit = ProcessRunner.Run("gcloud",
                $"secrets versions destroy {number} --secret={secretId} --project={_s.Gcp.ProjectId} --quiet");

            if (exit == 0)
                destroyed++;
            else
                Console.WriteLine($"  ! could not destroy {secretId} version {number} (gcloud exited {exit}).");
        }

        if (destroyed > 0)
            Console.WriteLine($"  pruned {destroyed} old version(s) of {secretId}");

        return destroyed;
    }

    /// <summary>
    /// The payload of the secret's current `latest`, or null when there is nothing to compare against
    /// (secret just created, no enabled version, or the read failed).
    ///
    /// Written to a temp file rather than read from stdout so the plaintext never crosses a console —
    /// `--format="value(payload.data)"` decodes and PRINTS the value, which would put private keys in
    /// the deploy log. Null is the safe answer for every failure: the caller then pushes a new
    /// version, which is exactly the old behaviour.
    /// </summary>
    private byte[]? ReadLatestPayload(string secretId)
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.Delete(tmp); // gcloud writes the file itself; GetTempFileName only reserved the name.

            var code = ProcessRunner.Run("gcloud",
                $"secrets versions access latest --secret={secretId} --project={_s.Gcp.ProjectId} --out-file=\"{tmp}\"");

            return code == 0 && File.Exists(tmp) ? File.ReadAllBytes(tmp) : null;
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
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

    /// <summary>Write the non-secret Terraform variables to &lt;env&gt;.tfvars.json (git-ignored).</summary>
    public void EmitTfvars(bool? deletionProtectionOverride = null)
    {
        var wl = _paths.TerraformWorkload;
        var project = _s.Gcp.ProjectId;

        var plainEnv = new Dictionary<string, string>();
        foreach (var (k, v) in _s.AppConfig)
            plainEnv[k] = v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString();

        var vars = new Dictionary<string, object?>
        {
            ["project_id"] = project,
            ["region"] = _s.Gcp.Region,
            ["name_prefix"] = _naming.NamePrefix,
            ["api_image"] = _naming.ImageTag("api"),
            ["consumers_image"] = _naming.ImageTag("consumers"),
            ["api_min_instances"] = _s.Gcp.ApiMinInstances,
            ["api_max_instances"] = _s.Gcp.ApiMaxInstances,
            ["consumer_count"] = _s.Gcp.ConsumerCount,
            ["enable_deletion_protection"] = deletionProtectionOverride ?? _s.Gcp.EnableDeletionProtection,
            // Monitoring is production-only and DERIVED, not authored: one less hand-set field to get
            // wrong, and a typo'd environment falls through to false rather than pointing a production
            // dashboard at nothing. Compare on Environment, NOT EnvCode — ToEnvCode maps anything
            // unrecognised to its first four alphanumerics, so "productionn" would still yield "prod".
            ["enable_dashboards"] = string.Equals(_s.Environment, "production", StringComparison.OrdinalIgnoreCase),
            ["billing_account_id"] = _s.Gcp.BillingAccountId,
            ["alert_email"] = _s.Gcp.AlertEmail,
            ["db_instance_suffix"] = _s.Gcp.DbInstanceSuffix,
            ["db_app_password"] = _s.Secrets.TryGetValue("ConnectionStrings:SpaceStroker", out var cs)
                ? ExtractPassword(cs)
                : "",
            ["front_bucket_name"] = _naming.FrontBucket,
            ["lp_bucket_name"] = _naming.LpBucket,
            ["dns_zone_name"] = _s.Gcp.DnsZoneName,
            ["domain_api"] = _s.Gcp.Domains.Api,
            ["domain_app"] = _s.Gcp.Domains.App,
            ["domain_lp"] = _s.Gcp.Domains.Lp,
            ["domain_lp_www"] = _s.Gcp.Domains.LpWww,
            ["plain_env"] = plainEnv,
            // Every resource is filterable by environment in the console.
            ["labels"] = new Dictionary<string, string>
            {
                ["app"] = "spacestroker",
                ["environment"] = _s.Environment,
                ["managed_by"] = "terraform",
            },
        };

        var path = TfVarsPath(wl);
        File.WriteAllText(path, JsonSerializer.Serialize(vars, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Wrote Terraform vars → {Path.GetFileName(path)} (git-ignored).");
    }

    /// <summary>
    /// Guarantees the persistent layer (state bucket + DNS zone) exists before any workload work.
    /// After a `--purge` teardown both are gone, so this re-bootstraps them: apply with LOCAL state
    /// (the gcs backend cannot be used until the bucket exists), then migrate the state into it.
    /// </summary>
    public void EnsurePersistentLayer()
    {
        var dir = _paths.TerraformPersistent;
        var tfvars = PersistentTfvarsPath();
        if (!File.Exists(tfvars))
            throw new DeployException(
                $"Missing {tfvars}. Copy production.tfvars.example, set protect_persistent plus the " +
                "env-scoped zone/bucket names, and re-run.");

        if (StateBucketExists())
        {
            TerraformInit(dir, "persistent");
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"State bucket gs://{_naming.StateBucket} not found — bootstrapping the persistent layer.");

        // Chicken-and-egg: this layer CREATES the gcs state bucket, so it can't use it as the backend
        // yet. Terraform 1.13 refuses `apply` while a `backend "gcs" {}` block is declared-but-
        // uninitialized (init -backend=false is not enough — apply still demands backend init). So we
        // drop a temporary override that swaps the backend to LOCAL for the bootstrap apply, then
        // remove it and migrate the local state into the freshly created gcs bucket.
        var bootstrapOverride = Path.Combine(dir, "zzz_bootstrap_override.tf");
        File.WriteAllText(bootstrapOverride, "terraform {\n  backend \"local\" {}\n}\n");
        try
        {
            ProcessRunner.RunOrThrow("terraform", "init -reconfigure", dir, TfEnv());
            ProcessRunner.RunOrThrow("terraform", $"apply -auto-approve -var-file=\"{tfvars}\"", dir, TfEnv());
        }
        finally
        {
            File.Delete(bootstrapOverride);
        }
        ProcessRunner.RunOrThrow("terraform",
            $"init -migrate-state -force-copy -backend-config=\"bucket={_naming.StateBucket}\" -backend-config=\"prefix=persistent\"",
            dir, TfEnv());

        var (_, nameServers) = ProcessRunner.Capture("terraform", "output -json dns_name_servers", dir, TfEnv());
        Console.WriteLine();
        Console.WriteLine($"!! Set these nameservers at the registrar (GoDaddy) for {_s.Gcp.DnsDomain.TrimEnd('.')}.");
        Console.WriteLine("!! Google-managed TLS certificates will NOT validate until the delegation is live:");
        Console.WriteLine($"   {nameServers.Trim()}");
        Console.WriteLine();
    }

    // --- Teardown ----------------------------------------------------------------

    private bool IsProduction => string.Equals(_s.Environment, "production", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A zone whose DNS name has two or fewer labels ("spacestroker.com.") is the registrable apex —
    /// the domain itself. Only sub-zones ("dev.spacestroker.com.") may ever be destroyed.
    /// </summary>
    internal static bool IsApexZone(string dnsDomain) =>
        dnsDomain.Trim().TrimEnd('.').Split('.', StringSplitOptions.RemoveEmptyEntries).Length <= 2;

    /// <summary>Default teardown (all environments): destroy the workload layer; the persistent layer
    /// (DNS zone + domain + state bucket) survives so a later `deploy` recreates + re-attaches.</summary>
    private void TeardownWorkloadOnly()
    {
        Console.WriteLine();
        Console.WriteLine("!! TEARDOWN — this DESTROYS the workload layer, including Cloud SQL and ALL its data.");
        Console.WriteLine("!! The persistent layer (DNS zone + domain + state bucket) is preserved.");
        if (!Confirm()) return;

        _naming.Validate();
        var leftovers = DestroyWorkload(purge: false);

        Console.WriteLine();
        if (leftovers.Count == 0)
        {
            Console.WriteLine("Workload destroyed. DNS zone + state bucket preserved — re-run `deploy` to recreate + re-attach.");
        }
        else
        {
            // Billable leftovers already threw inside DestroyWorkload, so this is the zero-cost case.
            Console.WriteLine(TeardownDecisions.ZeroCostWarning(leftovers, _s.Environment));
        }

        Console.WriteLine("!! Cloud SQL instance names stay reserved ~7 days — to redeploy sooner, bump");
        Console.WriteLine($"!! gcp.dbInstanceSuffix in secrets.{_s.Environment}.json (e.g. \"2\").");
    }

    /// <summary>
    /// Opt-in FULL wipe (CLI `--purge`, non-production only): destroy EVERYTHING Terraform created for
    /// this environment — the workload layer AND the persistent layer (DNS zone + state bucket) — then
    /// sweep orphan secrets and the locally generated files. Only the registrar's domain registration
    /// survives. The default `teardown` (no `--purge`) keeps the persistent layer.
    /// </summary>
    private void TeardownTotal()
    {
        if (IsProduction)
            throw new DeployException("Total teardown is blocked for the production environment.");

        if (IsApexZone(_s.Gcp.DnsDomain))
            throw new DeployException(
                $"Refusing to destroy the persistent layer: dnsDomain \"{_s.Gcp.DnsDomain}\" is the registrable " +
                "apex. Only sub-zones (e.g. \"dev.spacestroker.com.\") may be destroyed. Check " +
                $"secrets.{_s.Environment}.json.");

        _naming.Validate();

        Console.WriteLine();
        Console.WriteLine($"!! TOTAL TEARDOWN of '{_s.Environment}' — this destroys EVERYTHING Terraform created:");
        Console.WriteLine("!!   - Cloud Run, VPC/NAT/connector, HTTPS LB + Cloud Armor, Artifact Registry (and images)");
        Console.WriteLine("!!   - Cloud SQL AND ALL ITS DATA, Memorystore, the site buckets, Secret Manager secrets");
        Console.WriteLine($"!!   - the Cloud DNS zone {_s.Gcp.DnsDomain}");
        Console.WriteLine($"!!   - the Terraform state bucket gs://{_naming.StateBucket}");
        Console.WriteLine("!! Only the registrar (GoDaddy) domain registration survives.");
        if (!Confirm()) return;

        var leftovers = DestroyWorkload(purge: true);

        // The state bucket about to be destroyed is the ONLY record of anything still in state — even
        // the zero-cost network resources. Destroying it now would orphan them beyond Terraform's reach.
        if (leftovers.Count > 0)
            throw new DeployException(TeardownDecisions.PurgeBlockedMessage(leftovers, _s.Environment));

        DestroyPersistent();
        SweepOrphanSecrets();
        CleanLocalArtifacts();

        Console.WriteLine();
        Console.WriteLine("Total teardown complete — nothing Terraform created remains for this environment.");
        Console.WriteLine($"!! Remove the now-dead NS delegation for {_s.Gcp.DnsDomain.TrimEnd('.')} at GoDaddy;");
        Console.WriteLine("!! a later `deploy` recreates the zone with DIFFERENT nameservers and prints them.");
        Console.WriteLine("!! Cloud SQL instance names stay reserved ~7 days — to redeploy sooner, bump");
        Console.WriteLine($"!! gcp.dbInstanceSuffix in secrets.{_s.Environment}.json (e.g. \"2\").");
    }

    private bool Confirm()
    {
        Console.Write($"Type the environment name ('{_s.Environment}') to confirm: ");
        var typed = Console.ReadLine();
        if (string.Equals(typed?.Trim(), _s.Environment, StringComparison.Ordinal))
            return true;

        Console.WriteLine("Confirmation did not match. Aborting — nothing was destroyed.");
        return false;
    }

    /// <summary>
    /// Destroys the workload layer and returns whatever is STILL in state (empty == fully destroyed).
    /// Throws when anything billable survives; a zero-cost network remainder is returned, not thrown,
    /// because it costs nothing and releases on its own.
    /// </summary>
    private IReadOnlyList<string> DestroyWorkload(bool purge)
    {
        EmitTfvars(deletionProtectionOverride: false);
        var wl = _paths.TerraformWorkload;
        TerraformInit(wl, "workload");

        // Clear Cloud SQL deletion-protection WITHOUT converging the stack. A FULL apply here is what
        // made a partial teardown un-resumable: it re-created everything the failed pass had destroyed
        // (and then died on an image the same pass had already deleted). The attribute is Terraform's
        // own guard, so this targeted apply is a state-only write — nothing is created in GCP.
        if (TeardownDecisions.Contains(ReadState(wl, TeardownDecisions.SqlInstanceAddress),
                                       TeardownDecisions.SqlInstanceAddress))
        {
            ProcessRunner.RunOrThrow("terraform",
                $"apply -auto-approve -target={TeardownDecisions.SqlInstanceAddress} -var-file=\"{TfVarsPath(wl)}\"",
                wl, TfEnv());
        }
        else
        {
            Console.WriteLine($"  {TeardownDecisions.SqlInstanceAddress} is not in state — skipping the " +
                              "pre-destroy apply (resuming a partial teardown).");
        }

        RunDestroyWithRetries(wl);

        var leftovers = ReadState(wl);
        if (leftovers.Count > 0 && !TeardownDecisions.IsZeroCostOnly(leftovers))
            throw new DeployException(
                TeardownDecisions.BillableLeftoverError(leftovers, _s.Environment, purge));

        return leftovers;
    }

    /// <summary>
    /// Runs `terraform destroy` up to <see cref="TeardownDecisions.DestroyAttempts"/> times. The known
    /// ordering failures (messaging DB still connected, service-networking producers still attached,
    /// the subnet still held by the serverless egress address) each clear once the blocking resource is
    /// really gone, so a blind re-pass beats matching GCP's error text — which we cannot read anyway,
    /// since ProcessRunner does not redirect stderr.
    ///
    /// A still-failing final pass does NOT throw here: a destroy can exit non-zero having removed
    /// everything that matters. What is left in state decides the outcome, not Terraform's exit code.
    /// </summary>
    private void RunDestroyWithRetries(string wl)
    {
        for (var attempt = 1; attempt <= TeardownDecisions.DestroyAttempts; attempt++)
        {
            var code = ProcessRunner.Run("terraform",
                $"destroy -auto-approve -var-file=\"{TfVarsPath(wl)}\"", wl, TfEnv());
            if (code == 0)
                return;
            if (attempt == TeardownDecisions.DestroyAttempts)
                return;

            var wait = TeardownDecisions.DestroyBackoff[attempt - 1];
            Console.WriteLine();
            Console.WriteLine($"!! destroy pass {attempt}/{TeardownDecisions.DestroyAttempts} exited {code} — " +
                              $"waiting {wait.TotalSeconds:0}s and retrying (dependency ordering usually clears).");
            Thread.Sleep(wait);
        }
    }

    /// <summary>
    /// Reads `terraform state list`, optionally filtered to one address. A non-zero exit ABORTS: a
    /// failed probe must never be mistaken for "nothing left", which would skip clearing Cloud SQL
    /// deletion-protection on an intact stack, or report a teardown that never happened.
    /// An empty but readable state exits 0 with no output — that is the normal post-destroy case.
    /// </summary>
    private IReadOnlyList<string> ReadState(string wl, string? address = null)
    {
        var args = address is null ? "state list" : $"state list {address}";
        var (code, stdout) = ProcessRunner.Capture("terraform", args, wl, TfEnv());
        if (code != 0)
            throw new DeployException(
                $"'terraform {args}' exited with code {code} — cannot tell what is still deployed. " +
                "Check the workload backend/state and re-run; nothing was destroyed by this step.");

        return TeardownDecisions.ParseStateList(stdout);
    }

    /// <summary>
    /// The state bucket holds the very state that describes it, so migrate that state to a LOCAL
    /// backend first; Terraform can then destroy the zone and the bucket in one pass (the ephemeral
    /// bucket sets force_destroy = true). An already-absent bucket means "already torn down".
    /// </summary>
    private void DestroyPersistent()
    {
        var dir = _paths.TerraformPersistent;
        var tfvars = PersistentTfvarsPath();
        if (!File.Exists(tfvars))
        {
            Console.WriteLine($"  {Path.GetFileName(tfvars)} not found — skipping the persistent destroy.");
            return;
        }

        if (!StateBucketExists())
        {
            Console.WriteLine($"  gs://{_naming.StateBucket} is already gone — persistent layer already torn down.");
            return;
        }

        TerraformInit(dir, "persistent");
        ProcessRunner.RunOrThrow("terraform", "init -migrate-state -force-copy -backend=false", dir, TfEnv());
        ProcessRunner.RunOrThrow("terraform", $"destroy -auto-approve -var-file=\"{tfvars}\"", dir, TfEnv());
    }

    /// <summary>
    /// Terraform owns the secret CONTAINERS it declares, but <see cref="SyncSecrets"/> also
    /// `gcloud secrets create`s every key in secrets.&lt;env&gt;.json — a key that drifted out of
    /// Terraform's list would survive the destroy. Delete whatever is left for this environment.
    /// Secret ids are printed; values are never read.
    /// </summary>
    private void SweepOrphanSecrets()
    {
        Console.WriteLine("Sweeping leftover Secret Manager secrets…");
        var (code, output) = ProcessRunner.Capture("gcloud",
            $"secrets list --project={_s.Gcp.ProjectId} --format=\"value(name)\" --filter=\"name~{_naming.NamePrefix}-\"");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (code == 0)
        {
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var id = line.Trim().Split('/').Last();
                if (id.Length > 0)
                    ids.Add(id);
            }
        }

        foreach (var key in _s.Secrets.Keys)
            ids.Add(_naming.SecretId(key));

        foreach (var id in ids.Where(i => i.StartsWith(_naming.NamePrefix + "-", StringComparison.Ordinal)).Order())
        {
            var exit = ProcessRunner.Run("gcloud", $"secrets delete {id} --project={_s.Gcp.ProjectId} --quiet");
            Console.WriteLine(exit == 0 ? $"  deleted secret {id}" : $"  secret {id} already absent");
        }
    }

    /// <summary>
    /// Remove only what the tool GENERATED. secrets.&lt;env&gt;.json and *.tfvars are the operator's
    /// source of truth and are never touched.
    /// </summary>
    private void CleanLocalArtifacts()
    {
        Console.WriteLine("Cleaning locally generated files…");
        foreach (var dir in new[] { _paths.TerraformWorkload, _paths.TerraformPersistent })
        {
            DeleteFileIfExists(Path.Combine(dir, _naming.TfVarsFileName));
            // Legacy name emitted before the per-environment split — remove it too, it would be
            // auto-loaded by every terraform run in this directory.
            DeleteFileIfExists(Path.Combine(dir, "production.auto.tfvars.json"));
            DeleteDirectoryIfExists(Path.Combine(dir, _naming.TfDataDir));
            DeleteDirectoryIfExists(Path.Combine(dir, ".terraform"));
            DeleteFileIfExists(Path.Combine(dir, "terraform.tfstate"));
            DeleteFileIfExists(Path.Combine(dir, "terraform.tfstate.backup"));
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        if (!File.Exists(path)) return;
        File.Delete(path);
        Console.WriteLine($"  removed {path}");
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (!Directory.Exists(path)) return;
        Directory.Delete(path, recursive: true);
        Console.WriteLine($"  removed {path}");
    }

    // --- Terraform / build helpers -----------------------------------------------

    /// <summary>TF_DATA_DIR is per environment so two environments never share a .terraform dir.</summary>
    private IDictionary<string, string> TfEnv() =>
        new Dictionary<string, string> { ["TF_DATA_DIR"] = _naming.TfDataDir };

    private string TfVarsPath(string dir) => Path.Combine(dir, _naming.TfVarsFileName);

    private string PersistentTfvarsPath() =>
        Path.Combine(_paths.TerraformPersistent, $"{_s.Environment}.tfvars");

    private bool StateBucketExists()
    {
        var (code, _) = ProcessRunner.Capture("gcloud",
            $"storage buckets describe gs://{_naming.StateBucket} --project={_s.Gcp.ProjectId} --format=\"value(name)\"");
        return code == 0;
    }

    private void TerraformInit(string dir, string prefix)
    {
        // -reconfigure: the backend bucket differs per environment, so a stale .terraform must not win.
        ProcessRunner.RunOrThrow("terraform",
            $"init -reconfigure -backend-config=\"bucket={_naming.StateBucket}\" -backend-config=\"prefix={prefix}\"",
            dir, TfEnv());
    }

    private void TerraformApply(string dir)
    {
        TerraformInit(dir, "workload");
        ProcessRunner.RunOrThrow("terraform", $"apply -auto-approve -var-file=\"{TfVarsPath(dir)}\"", dir, TfEnv());
    }

    private void BuildAndPushImage(string image, string dockerfileRelative)
    {
        var tag = _naming.ImageTag(image);
        var context = Path.Combine(_paths.BackendRepo, "src");
        ProcessRunner.RunOrThrow("docker",
            $"build -t {tag} -f \"{Path.Combine(context, dockerfileRelative)}\" \"{context}\"");
        ProcessRunner.RunOrThrow("docker", $"push {tag}");
    }

    private void UpdateCloudRun(string service, string image)
    {
        ProcessRunner.RunOrThrow("gcloud",
            $"run services update {service} --project={_s.Gcp.ProjectId} --region={_s.Gcp.Region} --image={_naming.ImageTag(image)}");
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

    private void UploadAndInvalidate(StaticSite site, string bucket, string distDir)
    {
        if (!Directory.Exists(distDir))
            throw new DeployException($"Build output not found: {distDir}. Did the build succeed?");

        // The plan is derived from what the build actually emitted, so a file class nobody enumerated
        // lands in the conservative 1h tier rather than being missed.
        var rootFiles = Directory.GetFiles(distDir)
            .Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToArray();
        var rootDirectories = Directory.GetDirectories(distDir)
            .Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToArray();
        // Any depth: Astro emits directory-style pages, so the lp's /cookies page is cookies/index.html.
        var htmlPaths = Directory.EnumerateFiles(distDir, "*.html", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(distDir, p).Replace('\\', '/'))
            .Order(StringComparer.Ordinal).ToArray();

        foreach (var step in StaticSiteCache.Plan(site, distDir, bucket, rootFiles, rootDirectories, htmlPaths))
            ProcessRunner.RunOrThrow("gsutil", step.Arguments);

        PruneRetainedAssets(site, bucket, distDir, rootDirectories);

        // CDN cache invalidation (best-effort; url-map name follows the module convention). Kept at
        // /*: it is the only thing that flushes edge copies cached under the OLD headers on the
        // deploy that first carries this change.
        ProcessRunner.Run("gcloud",
            $"compute url-maps invalidate-cdn-cache {_naming.UrlMap} --project={_s.Gcp.ProjectId} --path=\"/*\" --async");
    }

    /// <summary>
    /// Records what this build published, then deletes assets no build inside the retention window
    /// still names. Assets are retained on purpose (a session running an older build can still fetch a
    /// lazily-loaded chunk), but "keep forever" is not a policy — see <see cref="BuildManifest"/> for
    /// why retirement time, not object age, is the thing being measured.
    /// </summary>
    private void PruneRetainedAssets(StaticSite site, string bucket, string distDir, IReadOnlyList<string> rootDirectories)
    {
        var hashedPrefixes = StaticSiteCache.HashedPrefixes(site)
            .Where(p => rootDirectories.Contains(p, StringComparer.Ordinal))
            .ToArray();
        if (hashedPrefixes.Length == 0)
            return;

        // 1. Record this build. The manifest is what makes "retired 24h ago" answerable at all.
        var published = hashedPrefixes
            .SelectMany(prefix => Directory.EnumerateFiles(Path.Combine(distDir, prefix), "*", SearchOption.AllDirectories))
            .Select(p => Path.GetRelativePath(distDir, p).Replace('\\', '/'))
            .Where(p => !StaticSiteCache.IsExcluded(p))
            .ToArray();

        var manifestName = BuildManifest.NameFor(DateTimeOffset.UtcNow);
        var localManifest = Path.Combine(Path.GetTempPath(), manifestName);
        File.WriteAllText(localManifest, BuildManifest.Render(published));
        try
        {
            ProcessRunner.RunOrThrow("gsutil",
                $"cp \"{localManifest}\" gs://{bucket}/{BuildManifest.Prefix}/{manifestName}");
        }
        finally
        {
            File.Delete(localManifest);
        }

        // 2. Which builds are still protected? A listing we cannot read prunes nothing (fail closed).
        var (listCode, listOut) = ProcessRunner.Capture("gsutil", $"ls gs://{bucket}/{BuildManifest.Prefix}/");
        if (listCode != 0)
        {
            Console.WriteLine("  prune skipped: could not list build manifests.");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var manifests = BuildManifest.ParseListing(listOut, bucket);
        var retained = BuildManifest.RetainedManifests(manifests, now);
        if (retained.Count == 0)
        {
            Console.WriteLine("  prune skipped: no retained manifests.");
            return;
        }

        // 3. The union of every retained build's assets — ONE `gsutil cat` PER MANIFEST. Reading them
        //    in a single call concatenates the bodies, so one manifest whose final line is not
        //    terminated fuses that line with the next manifest's first line and silently drops both
        //    from the keep-set (which pruned a freshly-published stylesheet). Render now always
        //    terminates, but manifests written before that fix still sit in the buckets for their
        //    retention window, so the reader must not depend on it either.
        var keep = new List<string>();
        foreach (var manifest in retained)
        {
            var (catCode, catOut) = ProcessRunner.Capture("gsutil", $"cat gs://{bucket}/{manifest}");
            if (catCode != 0)
            {
                Console.WriteLine("  prune skipped: could not read a retained manifest.");
                return;
            }
            keep.AddRange(BuildManifest.ParseManifestBodies(catOut));
        }

        // 4. Everything currently under the asset prefixes, minus what the retained builds name.
        var inBucket = new List<string>();
        foreach (var prefix in hashedPrefixes)
        {
            var (lsCode, lsOut) = ProcessRunner.Capture("gsutil", $"ls -r gs://{bucket}/{prefix}");
            if (lsCode != 0)
            {
                Console.WriteLine($"  prune skipped: could not list gs://{bucket}/{prefix}.");
                return;
            }
            inBucket.AddRange(BuildManifest.ParseListing(lsOut, bucket));
        }

        // `published` is protected outright: whatever the manifests say, this deploy must never delete
        // what it just uploaded.
        var stale = BuildManifest.PruneTargets(inBucket, keep, published);
        var expired = BuildManifest.ExpiredManifests(manifests, now);
        var toDelete = stale.Concat(expired).Select(p => $"gs://{bucket}/{p}").ToArray();

        if (toDelete.Length == 0)
        {
            Console.WriteLine("  prune: nothing to remove.");
            return;
        }

        Console.WriteLine($"  prune: removing {stale.Count} retired asset(s) and {expired.Count} expired manifest(s).");
        foreach (var batch in BuildManifest.Batch(toDelete))
            ProcessRunner.RunOrThrow("gsutil", $"-m rm {string.Join(' ', batch)}");
    }

    // --- Helpers -----------------------------------------------------------------

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
