using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceStroker.Deploy;

/// <summary>
/// The unversioned local secrets.&lt;env&gt;.json — the single source of truth for a deploy.
/// Feeds Secret Manager (secrets), Terraform vars (gcp + appConfig), and the front/lp build.
/// </summary>
public sealed class SecretsFile
{
    [JsonPropertyName("environment")] public string Environment { get; set; } = "production";
    [JsonPropertyName("gcp")] public GcpSection Gcp { get; set; } = new();
    [JsonPropertyName("secrets")] public Dictionary<string, string> Secrets { get; set; } = new();
    [JsonPropertyName("appConfig")] public Dictionary<string, JsonElement> AppConfig { get; set; } = new();
    [JsonPropertyName("frontBuild")] public Dictionary<string, string> FrontBuild { get; set; } = new();

    public static SecretsFile Load(string path)
    {
        if (!File.Exists(path))
            throw new DeployException($"Secrets file not found: {path}. Copy deploy/secrets.example.json and fill it in.");

        var json = File.ReadAllText(path);
        var parsed = JsonSerializer.Deserialize<SecretsFile>(json, JsonOpts)
            ?? throw new DeployException($"Could not parse {path}.");

        if (string.IsNullOrWhiteSpace(parsed.Gcp.ProjectId) || parsed.Gcp.ProjectId.StartsWith("your-"))
            throw new DeployException($"{path}: gcp.projectId is still a placeholder — fill in the real project id.");

        if (string.IsNullOrWhiteSpace(parsed.Environment))
            throw new DeployException($"{path}: 'environment' is required — it drives every resource name.");

        // The state bucket is DERIVED (spacestroker-tfstate-<envCode>-<projectId>) so a development
        // teardown can delete its own bucket without ever touching production state. An authored value
        // would silently disagree with the backend config the tool passes to terraform init.
        if (!string.IsNullOrWhiteSpace(parsed.Gcp.StateBucket))
            throw new DeployException(
                $"{path}: 'gcp.stateBucket' is no longer authored — it is derived as " +
                "spacestroker-tfstate-<envCode>-<projectId>. Remove the field.");

        // Fails fast on a bad 'environment' before anything else reads it.
        EnvNaming.ToEnvCode(parsed.Environment);

        return parsed;
    }

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

public sealed class GcpSection
{
    [JsonPropertyName("projectId")] public string ProjectId { get; set; } = "";
    [JsonPropertyName("region")] public string Region { get; set; } = "us-central1";
    /// <summary>Legacy field — kept only so an old secrets file parses and trips the "remove it" error.</summary>
    [JsonPropertyName("stateBucket")] public string StateBucket { get; set; } = "";
    [JsonPropertyName("dnsZoneName")] public string DnsZoneName { get; set; } = "";
    [JsonPropertyName("dnsDomain")] public string DnsDomain { get; set; } = "";
    [JsonPropertyName("domains")] public Domains Domains { get; set; } = new();
    [JsonPropertyName("consumerCount")] public int ConsumerCount { get; set; } = 1;
    [JsonPropertyName("apiMinInstances")] public int ApiMinInstances { get; set; } = 1;
    [JsonPropertyName("apiMaxInstances")] public int ApiMaxInstances { get; set; } = 10;
    [JsonPropertyName("enableDeletionProtection")] public bool EnableDeletionProtection { get; set; } = true;

    /// <summary>
    /// Billing account id for the spend budget (e.g. "01A2B3-C4D5E6-F7G8H9"). Empty ⇒ no budget.
    /// A budget is created against the BILLING ACCOUNT, which sits above the project, so this also
    /// needs roles/billing.costsManager granted there — no project-level role can do it.
    /// Plain config rather than a credential; still never commit a real value.
    /// </summary>
    [JsonPropertyName("billingAccountId")] public string BillingAccountId { get; set; } = "";

    /// <summary>Destination for budget threshold mail. Empty ⇒ no notification channel, no budget.</summary>
    [JsonPropertyName("alertEmail")] public string AlertEmail { get; set; } = "";

    /// <summary>
    /// Optional suffix for the Cloud SQL instance name. GCP reserves a deleted instance name for ~7
    /// days, so redeploying an environment inside that window needs a new name — bump this (e.g. "2")
    /// instead of waiting.
    /// </summary>
    [JsonPropertyName("dbInstanceSuffix")] public string DbInstanceSuffix { get; set; } = "";
}

public sealed class Domains
{
    [JsonPropertyName("api")] public string Api { get; set; } = "";
    [JsonPropertyName("app")] public string App { get; set; } = "";
    [JsonPropertyName("lp")] public string Lp { get; set; } = "";
    [JsonPropertyName("lpWww")] public string LpWww { get; set; } = "";
}
