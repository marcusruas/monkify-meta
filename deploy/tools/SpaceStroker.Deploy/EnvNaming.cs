using System.Text.RegularExpressions;

namespace SpaceStroker.Deploy;

/// <summary>
/// Single source of truth for every environment-scoped GCP id. Development and production share one
/// GCP project, so every project-global name carries the env code (spacestroker-dev-*,
/// spacestroker-prod-*) — otherwise the two environments collide on names AND on Terraform state.
/// Ids are validated against GCP's length caps BEFORE terraform runs: a half-applied stack is worse
/// than a fast failure.
/// </summary>
public sealed class EnvNaming
{
    // GCP id length caps. Service account account_id 6-30; Serverless VPC connector name 25;
    // GCS bucket 3-63; Memorystore instance id 40; Cloud Run service 49; Artifact Registry repo 63;
    // Cloud SQL instance 98; Cloud DNS managed zone 63; Secret Manager secret id 255.
    private const int ServiceAccountMax = 30;
    private const int VpcConnectorMax = 25;
    private const int BucketMax = 63;
    private const int RedisMax = 40;
    private const int CloudRunMax = 49;
    private const int ArtifactRegistryMax = 63;
    private const int CloudSqlMax = 98;
    private const int DnsZoneMax = 63;
    private const int SecretIdMax = 255;

    private readonly string _projectId;
    private readonly string _region;
    private readonly IEnumerable<string> _secretKeys;

    public string Environment { get; }
    public string EnvCode { get; }
    public string NamePrefix => $"spacestroker-{EnvCode}";

    private EnvNaming(string environment, string envCode, string projectId, string region, IEnumerable<string> secretKeys)
    {
        Environment = environment;
        EnvCode = envCode;
        _projectId = projectId;
        _region = region;
        _secretKeys = secretKeys;
    }

    public static EnvNaming From(SecretsFile s) => new(
        s.Environment,
        ToEnvCode(s.Environment),
        s.Gcp.ProjectId,
        s.Gcp.Region,
        s.Secrets.Keys);

    /// <summary>development -> dev, production -> prod, staging -> stg; else the first 4 alphanumerics.</summary>
    public static string ToEnvCode(string environment)
    {
        var env = (environment ?? "").Trim().ToLowerInvariant();
        var code = env switch
        {
            "development" => "dev",
            "production" => "prod",
            "staging" => "stg",
            "" => throw new DeployException("secrets.<env>.json: 'environment' is empty — it drives every resource name."),
            _ => new string(env.Where(char.IsLetterOrDigit).Take(4).ToArray()),
        };

        if (!Regex.IsMatch(code, "^[a-z][a-z0-9]{1,4}$"))
            throw new DeployException(
                $"secrets.<env>.json: 'environment' = \"{environment}\" yields the env code \"{code}\", which must " +
                "be 2-5 characters, start with a letter and contain only lowercase letters and digits.");

        return code;
    }

    // --- Derived ids -----------------------------------------------------------------

    /// <summary>Per-environment remote state — a development teardown deletes its OWN bucket.</summary>
    public string StateBucket => $"spacestroker-tfstate-{EnvCode}-{_projectId}";

    public string FrontBucket => $"{NamePrefix}-front-{_projectId}";
    public string LpBucket => $"{NamePrefix}-lp-{_projectId}";
    public string RegistryRepo => NamePrefix;
    public string ApiService => $"{NamePrefix}-api";
    public string ConsumersService => $"{NamePrefix}-consumers";
    public string UrlMap => $"{NamePrefix}-urlmap";
    public string DnsZoneName => $"{NamePrefix}-zone";
    public string VpcConnector => $"{NamePrefix}-vpcconn";
    public string SqlInstance => $"{NamePrefix}-pg";
    public string RedisInstance => $"{NamePrefix}-redis";

    /// <summary>Per-environment Terraform working dir — two environments must never share .terraform.</summary>
    public string TfDataDir => $".terraform-{EnvCode}";

    /// <summary>
    /// Deliberately NOT *.auto.tfvars.json: Terraform auto-loads every such file in the directory, so
    /// two environments' files would both load and the last one would silently win. Always passed
    /// explicitly with -var-file.
    /// </summary>
    public string TfVarsFileName => $"{Environment}.tfvars.json";

    /// <summary>Secret Manager ids are project-global, so they carry the env prefix too.</summary>
    public string SecretId(string configKey) => $"{NamePrefix}-{configKey.Replace(':', '-')}";

    public string ImageTag(string image) => $"{_region}-docker.pkg.dev/{_projectId}/{RegistryRepo}/{image}:latest";

    // --- Validation ------------------------------------------------------------------

    /// <summary>Every derived id with the cap it must respect and whether it fits.</summary>
    public IReadOnlyList<(string Kind, string Id, int Limit, bool Ok)> DerivedIds()
    {
        var ids = new List<(string Kind, string Id, int Limit)>
        {
            ("cloud-run", ApiService, CloudRunMax),
            ("cloud-run", ConsumersService, CloudRunMax),
            ("service-account", ApiService, ServiceAccountMax),
            ("service-account", ConsumersService, ServiceAccountMax),
            ("vpc-connector", VpcConnector, VpcConnectorMax),
            ("cloud-sql", SqlInstance, CloudSqlMax),
            ("memorystore", RedisInstance, RedisMax),
            ("artifact-registry", RegistryRepo, ArtifactRegistryMax),
            ("gcs-bucket", StateBucket, BucketMax),
            ("gcs-bucket", FrontBucket, BucketMax),
            ("gcs-bucket", LpBucket, BucketMax),
            ("dns-zone", DnsZoneName, DnsZoneMax),
        };

        foreach (var key in _secretKeys)
            ids.Add(("secret", SecretId(key), SecretIdMax));

        return ids.Select(i => (i.Kind, i.Id, i.Limit, i.Id.Length <= i.Limit)).ToList();
    }

    /// <summary>Throws on the first id that exceeds its GCP cap or breaks the charset. Runs before every apply/destroy.</summary>
    public void Validate()
    {
        foreach (var (kind, id, limit, ok) in DerivedIds())
        {
            if (!ok)
                throw new DeployException(
                    $"Derived {kind} id \"{id}\" is {id.Length} characters — GCP allows at most {limit}. " +
                    $"Shorten 'environment' (env code is currently \"{EnvCode}\") or the project id in " +
                    $"secrets.{Environment}.json.");

            // Cloud Run / service accounts / connectors / buckets all want lowercase alphanumerics and
            // hyphens, starting with a letter. Secret ids also allow underscores.
            var pattern = kind == "secret" ? "^[a-zA-Z][a-zA-Z0-9_-]*$" : "^[a-z][a-z0-9-]*$";
            if (!Regex.IsMatch(id, pattern))
                throw new DeployException(
                    $"Derived {kind} id \"{id}\" is not a valid GCP id (expected {pattern}). Check the project " +
                    $"id and 'environment' in secrets.{Environment}.json.");
        }
    }

    /// <summary>
    /// The DNS zone name is authored in secrets.&lt;env&gt;.json + the persistent tfvars (Terraform needs it
    /// in both), so it can drift from the convention. Warn rather than abort — an existing zone with a
    /// different name is legitimate.
    /// </summary>
    public string? DnsZoneNameWarning(string authored) =>
        string.Equals(authored, DnsZoneName, StringComparison.Ordinal)
            ? null
            : $"gcp.dnsZoneName is \"{authored}\" but the convention for this environment is \"{DnsZoneName}\". " +
              "That is allowed, but keep it in sync with the persistent layer's tfvars.";
}
