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
    [JsonPropertyName("stateBucket")] public string StateBucket { get; set; } = "";
    [JsonPropertyName("dnsZoneName")] public string DnsZoneName { get; set; } = "";
    [JsonPropertyName("dnsDomain")] public string DnsDomain { get; set; } = "";
    [JsonPropertyName("domains")] public Domains Domains { get; set; } = new();
    [JsonPropertyName("consumerCount")] public int ConsumerCount { get; set; } = 1;
    [JsonPropertyName("apiMinInstances")] public int ApiMinInstances { get; set; } = 1;
    [JsonPropertyName("apiMaxInstances")] public int ApiMaxInstances { get; set; } = 10;
    [JsonPropertyName("enableDeletionProtection")] public bool EnableDeletionProtection { get; set; } = true;
}

public sealed class Domains
{
    [JsonPropertyName("api")] public string Api { get; set; } = "";
    [JsonPropertyName("app")] public string App { get; set; } = "";
    [JsonPropertyName("lp")] public string Lp { get; set; } = "";
    [JsonPropertyName("lpWww")] public string LpWww { get; set; } = "";
}
