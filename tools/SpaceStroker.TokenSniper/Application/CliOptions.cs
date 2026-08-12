using SpaceStroker.TokenSniper.Domain;

namespace SpaceStroker.TokenSniper.Application;

/// <summary>Hand-rolled argument parsing, matching SpaceStroker.WalletFunder's CliOptions.</summary>
public sealed class CliOptions
{
    private static readonly string[] Commands = ["snipe", "status"];

    public string Command { get; private set; } = "snipe";
    public string SecretsPath { get; private set; } = "";
    public string ReceiptsPath { get; private set; } = "";
    public string ReportsRootPath { get; private set; } = "";
    public string? PoolPath { get; private set; }
    public bool Execute { get; private set; }
    public int? SpreadMinutes { get; private set; }
    public List<string> OnlyAliases { get; } = [];
    public bool ShowHelp { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();

        // Default the config paths to the tools folder: bin/<cfg>/net8.0 sits four levels below it.
        var toolsRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        options.SecretsPath = Path.Combine(toolsRoot, "secrets.json");
        options.ReceiptsPath = Path.Combine(toolsRoot, "Receipts", "snipe-receipts.json");
        options.ReportsRootPath = Path.Combine(toolsRoot, "Receipts", "reports");

        if (args.Length > 0 && args[0] is "-h" or "--help" or "help")
        {
            options.ShowHelp = true;
            return options;
        }

        var i = 0;
        if (args.Length > 0 && Commands.Contains(args[0]))
        {
            options.Command = args[0];
            i = 1;
        }

        for (; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--secrets":
                    options.SecretsPath = Next(args, ref i, "--secrets");
                    break;
                case "--pool":
                    options.PoolPath = Next(args, ref i, "--pool");
                    break;
                case "--receipts":
                    options.ReceiptsPath = Next(args, ref i, "--receipts");
                    break;
                case "--reports":
                    options.ReportsRootPath = Next(args, ref i, "--reports");
                    break;
                case "--execute":
                    options.Execute = true;
                    break;
                case "--spread-minutes":
                    var raw = Next(args, ref i, "--spread-minutes");
                    if (!int.TryParse(raw, out var minutes) || minutes < 0)
                        throw new UsageException($"--spread-minutes needs a non-negative whole number (got '{raw}').");
                    options.SpreadMinutes = minutes;
                    break;
                case "--wallet":
                    options.OnlyAliases.Add(Next(args, ref i, "--wallet"));
                    break;
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    return options;
                default:
                    throw new UsageException($"Unknown option '{args[i]}'.");
            }
        }

        if (options.Command == "status" && options.Execute)
            throw new UsageException("`status` is read-only and cannot take --execute.");

        return options;
    }

    private static string Next(string[] args, ref int i, string option)
    {
        if (i + 1 >= args.Length) throw new UsageException($"{option} needs a value.");
        return args[++i];
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            spacestroker-snipe-token [command] [options]

            Commands:
              snipe       (default) build the plan and - with --execute - buy the pool up to target.
                          Without --execute it is a dry run: ladder table + full preflight, nothing submitted.
              status      read-only: each wallet's holding vs target, from on-chain state. Never submits.

            Options:
              --secrets <path>       secrets file (default: tools/secrets.json)
              --pool <path>          load the wallet array from a standalone JSON file instead of secrets.wallets
              --receipts <path>      receipts file (default: tools/Receipts/snipe-receipts.json)
              --reports <dir>        report output root (default: tools/Receipts/reports)
              --execute              actually submit buys. Without it, `snipe` is a dry run.
              --spread-minutes <n>   override snipe.spreadMinutes for this run
              --wallet <alias>       limit the run to this wallet (repeatable)
              -h | --help            usage

            Exit codes: 0 every targeted wallet reached its target / dry run / empty pool ·
                        1 anything short, failed or unresolved · 2 usage error.
            """);
    }
}
