using SpaceStroker.Nightfall.Domain;

namespace SpaceStroker.Nightfall.Application;

/// <summary>Hand-rolled argument parsing, matching SpaceStroker.WalletFunder/Application/CliOptions.cs.</summary>
public sealed class CliOptions
{
    public string SecretsPath { get; private set; } = "";
    public string ReceiptsPath { get; private set; } = "";
    public int Port { get; private set; } = 5177;
    public bool OpenBrowser { get; private set; } = true;
    public bool ShowHelp { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();

        // Default the config paths to the tools folder: bin/<cfg>/net8.0 sits four levels below it.
        var toolsRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        o.SecretsPath = Path.Combine(toolsRoot, "secrets.json");
        o.ReceiptsPath = Path.Combine(toolsRoot, "Receipts", "nightfall-receipts.json");

        if (args.Length > 0 && args[0] is "-h" or "--help" or "help")
        {
            o.ShowHelp = true;
            return o;
        }

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--secrets":
                    o.SecretsPath = Next(args, ref i, "--secrets");
                    break;
                case "--receipts":
                    o.ReceiptsPath = Next(args, ref i, "--receipts");
                    break;
                case "--port":
                    var raw = Next(args, ref i, "--port");
                    if (!int.TryParse(raw, out var port) || port is < 1 or > 65535)
                        throw new UsageException($"--port must be a number between 1 and 65535 (was '{raw}').");
                    o.Port = port;
                    break;
                case "--no-open":
                    o.OpenBrowser = false;
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
        spacestroker-nightfall — dashboard over a Solana wallet pool: see what each wallet holds and
        sell it, one wallet or many concurrently.

        The host binds to 127.0.0.1 only and holds the pool's private keys in-process; the browser
        never receives one. It starts in SIMULATE mode — nothing is broadcast until you switch to LIVE.

        Options:
          --secrets <path>    secrets file (default: tools/secrets.json, 'nightfall' section)
          --receipts <path>   receipts file (default: tools/Receipts/nightfall-receipts.json)
          --port <n>          localhost port (default: 5177)
          --no-open           do not launch the browser on start
          -h, --help          this message

        ⚠️ A sell is irreversible. Rehearse a drag-select in SIMULATE before switching to LIVE.
        """);
}
