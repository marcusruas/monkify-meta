using SpaceStroker.Deploy;

// spacestroker-deploy — manual GCP deploy driver for SpaceStroker (Terraform + Cloud Run + static sites).
// Usage:
//   spacestroker-deploy deploy [api|consumers|front|lp]   deploy one component (or all if omitted)
//   spacestroker-deploy teardown                          destroy the workload layer (keeps the domain)
// Options:
//   --env <name>          environment (default: production) → secrets.<name>.json
//   --secrets <path>      explicit secrets file path
//   --workspace <path>    workspace root (else auto-detected)

try
{
    var opts = CliOptions.Parse(args);
    if (opts.ShowHelp)
    {
        CliOptions.PrintUsage();
        return 0;
    }

    var paths = RepoPaths.Resolve(opts.WorkspaceRoot);
    var secretsPath = opts.SecretsPath
        ?? Path.Combine(paths.MetaRepo, "deploy", $"secrets.{opts.Environment}.json");
    var secrets = SecretsFile.Load(secretsPath);
    var deployer = new Deployer(secrets, paths);

    switch (opts.Command)
    {
        case "deploy" when opts.Component is null:
            deployer.DeployAll();
            break;
        case "deploy":
            deployer.SyncSecrets();
            deployer.EmitTfvars();
            deployer.DeployComponent(opts.Component!);
            break;
        case "teardown":
            deployer.Teardown();
            break;
        default:
            CliOptions.PrintUsage();
            return 2;
    }

    return 0;
}
catch (DeployException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

file sealed class CliOptions
{
    public string Command { get; private set; } = "";
    public string? Component { get; private set; }
    public string Environment { get; private set; } = "production";
    public string? SecretsPath { get; private set; }
    public string? WorkspaceRoot { get; private set; }
    public bool ShowHelp { get; private set; }

    private static readonly HashSet<string> Components = ["api", "consumers", "front", "lp"];

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            o.ShowHelp = true;
            return o;
        }

        o.Command = args[0];
        var i = 1;
        if (o.Command == "deploy" && args.Length > 1 && Components.Contains(args[1]))
        {
            o.Component = args[1];
            i = 2;
        }

        for (; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--env": o.Environment = Next(args, ref i); break;
                case "--secrets": o.SecretsPath = Next(args, ref i); break;
                case "--workspace": o.WorkspaceRoot = Next(args, ref i); break;
                case "-h" or "--help": o.ShowHelp = true; break;
                default: throw new DeployException($"Unknown argument: {args[i]}");
            }
        }

        if (o.Command is not ("deploy" or "teardown"))
            throw new DeployException($"Unknown command '{o.Command}'. Use 'deploy' or 'teardown'.");

        return o;
    }

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length) throw new DeployException($"Missing value after {args[i]}.");
        return args[++i];
    }

    public static void PrintUsage()
    {
        Console.WriteLine(
            """
            spacestroker-deploy — manual GCP deploy driver

            Usage:
              spacestroker-deploy deploy [api|consumers|front|lp]  deploy one component (or all if omitted)
              spacestroker-deploy teardown                         destroy the workload layer (keeps the domain)

            Options:
              --env <name>        environment (default: production) → secrets.<name>.json
              --secrets <path>    explicit secrets file path
              --workspace <path>  workspace root (else auto-detected)

            Prerequisites: gcloud (authenticated), docker, terraform, gsutil, yarn, npm on PATH.
            See deploy/README.md.
            """);
    }
}
