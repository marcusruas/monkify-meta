using System.Text.RegularExpressions;

namespace SpaceStroker.Deploy;

/// <summary>
/// Guards the one asymmetry in <c>secrets.&lt;env&gt;.json</c>.
///
/// The <c>appConfig</c> block reaches Cloud Run on its own: the tool emits it into the tfvars and
/// Terraform passes the whole map through as <c>var.plain_env</c>, so a key added there just works.
/// The <c>secrets</c> block does NOT. Each entry needs TWO hand-written lines in
/// <c>terraform/workload/main.tf</c> — one in <c>base_secret_ids</c> to grant accessor IAM, and one
/// in <c>base_secret_env</c> to mount it as an env var.
///
/// Miss them and nothing fails. <see cref="Deployer.SyncSecrets"/> still creates the secret and
/// pushes the real value, `terraform apply` still succeeds, and the deploy still prints
/// "Deploy complete." — but the container never receives the variable, so the app silently falls
/// back to whatever its committed appsettings say. For a fail-closed setting that is an invisible
/// no-op, which is the worst possible shape for a money path.
///
/// ⚠️ THIS HAS SHIPPED TWICE. Orbit's three keys (#123, caught before release) and the house seed
/// key (#168) — the latter reached PRODUCTION on 2026-08-13 and turned house seeding into a silent
/// no-op until someone noticed rounds were never seeded. Nothing else catches it: `validate` checks
/// id LENGTH, so an unbound secret prints a cheerful PASS.
/// </summary>
public static class SecretBindingCheck
{
    /// <summary>
    /// Secret Manager ids are the config key with colons swapped for dashes — the same transform
    /// <see cref="EnvNaming.SecretId"/> applies before prefixing the environment. The env-scoped
    /// prefix is added by Terraform itself (<c>"${local.name_prefix}-${id}"</c>), so the ids written
    /// in main.tf are UNPREFIXED and compare directly against this.
    /// </summary>
    public static string ToSecretId(string configKey) => configKey.Replace(':', '-');

    /// <summary>
    /// Returns the secrets that main.tf does not fully wire up, as human-readable findings. Empty
    /// means every key is both IAM-granted and mounted.
    ///
    /// Deliberately reports the two lists separately: an id present in <c>base_secret_env</c> but not
    /// <c>base_secret_ids</c> mounts a variable the service account cannot read, which fails the
    /// revision at START-UP rather than silently — a different and much louder bug.
    /// </summary>
    public static IReadOnlyList<string> FindUnbound(IEnumerable<string> secretKeys, string workloadMainTf)
    {
        var ids = Block(workloadMainTf, "base_secret_ids", '[', ']');
        var env = Block(workloadMainTf, "base_secret_env", '{', '}');

        var findings = new List<string>();

        foreach (var key in secretKeys)
        {
            var id = ToSecretId(key);
            var quoted = $"\"{id}\"";

            var hasIam = ids.Contains(quoted, StringComparison.Ordinal);
            var hasEnv = env.Contains(quoted, StringComparison.Ordinal);

            if (hasIam && hasEnv)
                continue;

            var missing = (hasIam, hasEnv) switch
            {
                (false, false) => "base_secret_ids AND base_secret_env",
                (false, true) => "base_secret_ids (no accessor IAM — the revision will fail to start)",
                _ => "base_secret_env (synced to Secret Manager but never mounted)",
            };

            findings.Add($"{key} → '{id}' is missing from {missing}");
        }

        return findings;
    }

    /// <summary>
    /// The text inside a named HCL collection. Brace-matched rather than regex'd to the closing
    /// delimiter so a future nested value cannot silently truncate the block and turn this guard into
    /// a source of false failures.
    /// </summary>
    private static string Block(string tf, string name, char open, char close)
    {
        // Regex.Escape already escapes '[' and '{', so the delimiter goes in bare — prefixing it with
        // a backslash here emits a LITERAL backslash before the interpolation hole and the pattern
        // then fails to parse at all.
        var header = Regex.Match(tf, $@"{Regex.Escape(name)}\s*=\s*{Regex.Escape(open.ToString())}");

        if (!header.Success)
            throw new DeployException(
                $"terraform/workload/main.tf has no '{name}' block. The secret-binding guard reads it to " +
                "check every secrets.<env>.json key is wired up; if the block was renamed, update " +
                nameof(SecretBindingCheck) + " with it.");

        var start = header.Index + header.Length - 1;
        var depth = 0;

        for (var i = start; i < tf.Length; i++)
        {
            if (tf[i] == open) depth++;
            else if (tf[i] == close && --depth == 0) return tf.Substring(start, i - start + 1);
        }

        throw new DeployException($"terraform/workload/main.tf: '{name}' is never closed.");
    }
}
