using SpaceStroker.WalletFunder.Domain;

namespace SpaceStroker.WalletFunder.Application;

/// <summary>Hand-rolled argument parsing, matching deploy/tools/SpaceStroker.Deploy/Program.cs.</summary>
public sealed class CliOptions
{
    private static readonly string[] Commands = ["fund", "status"];

    public string Command { get; private set; } = "fund";
    public string SecretsPath { get; private set; } = "";
    public string ReceiptsPath { get; private set; } = "";
    public string? PoolPath { get; private set; }
    public bool Execute { get; private set; }
    public bool ForcePlanChange { get; private set; }
    public int? SpreadMinutes { get; private set; }
    public bool ShowHelp { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();

        // Default the config paths to the tools folder: bin/<cfg>/net8.0 sits four levels below it.
        var toolsRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        o.SecretsPath = Path.Combine(toolsRoot, "secrets.json");
        o.ReceiptsPath = Path.Combine(toolsRoot, "receipts.json");

        if (args.Length > 0 && args[0] is "-h" or "--help" or "help")
        {
            o.ShowHelp = true;
            return o;
        }

        var i = 0;
        if (args.Length > 0 && Commands.Contains(args[0]))
        {
            o.Command = args[0];
            i = 1;
        }

        for (; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--secrets":
                    o.SecretsPath = Next(args, ref i, "--secrets");
                    break;
                case "--receipts":
                    o.ReceiptsPath = Next(args, ref i, "--receipts");
                    break;
                case "--pool":
                    o.PoolPath = Next(args, ref i, "--pool");
                    break;
                case "--execute":
                    o.Execute = true;
                    break;
                case "--force-plan-change":
                    o.ForcePlanChange = true;
                    break;
                case "--spread-minutes":
                    var raw = Next(args, ref i, "--spread-minutes");
                    if (!int.TryParse(raw, out var minutes) || minutes < 0)
                        throw new UsageException($"--spread-minutes must be a non-negative integer (was '{raw}').");
                    o.SpreadMinutes = minutes;
                    break;
                default:
                    throw new UsageException($"Unknown option '{args[i]}'. Run with --help.");
            }
        }

        return o;
    }

    private static string Next(string[] args, ref int i, string option)
    {
        if (i + 1 >= args.Length) throw new UsageException($"{option} needs a value.");
        return args[++i];
    }

    public static void PrintUsage()
        => Console.WriteLine("""
        spacestroker-fund-wallets — fund Solana wallets with SOL from a Coinbase account.

        Commands:
          fund     (default) build the plan; with --execute, fund the pool. Without --execute it is a
                   dry run: plan table + full preflight, nothing sent.
          status   read-only: poll pending transactions + re-check on-chain confirmation. Never sends.

        Options:
          --secrets <path>       secrets file (default: tools/secrets.json)
          --pool <path>          wallet array from a standalone JSON file instead of secrets.wallets
          --receipts <path>      receipts file (default: tools/receipts.json)
          --execute              actually send (also requires typing the batch total to confirm)
          --spread-minutes <n>   override funding.spreadMinutes for this run
          --force-plan-change    resume against drifted receipts (DANGEROUS — can double-fund)
          -h, --help             this message
        """);
}
