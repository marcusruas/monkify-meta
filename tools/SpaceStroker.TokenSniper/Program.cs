using SpaceStroker.TokenSniper.Application;
using SpaceStroker.TokenSniper.Domain;
using SpaceStroker.TokenSniper.Infrastructure;

// spacestroker-snipe-token — drives a pool of wallets we control to a configured share of our own
// pump.fun token's supply: one buy per wallet, ascending alias order, jittered across a window.
// Usage:
//   spacestroker-snipe-token snipe             build the plan; with --execute, buy the pool
//   spacestroker-snipe-token status            read-only: holdings vs targets. Never submits.
// See tools/README.md and monkify-meta/specs/ghost-wallet-snipe.md.

try
{
    var options = CliOptions.Parse(args);
    if (options.ShowHelp)
    {
        CliOptions.PrintUsage();
        return 0;
    }

    // Card #167 moved the default receipts path into tools/Receipts/. Unlike the funder this is only
    // an audit-trail note, not a money risk: this tool recomputes the shortfall from live on-chain
    // balances, so on-chain state — not the receipts file — is the resume truth.
    var legacySnipeReceipts = Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")), "snipe-receipts.json");
    if (!File.Exists(options.ReceiptsPath) && File.Exists(legacySnipeReceipts))
        Console.WriteLine($"note: a pre-refactor receipts file is still at {legacySnipeReceipts}; " +
                          $"this run writes to {options.ReceiptsPath}. On-chain state is the resume truth, " +
                          $"so this is an audit-trail note, not a money risk.");

    var secrets = SecretsFile.Load(options.SecretsPath);

    var sniper = new Sniper(secrets, new SniperOptions(
        ReceiptsPath: options.ReceiptsPath,
        ReportsRootPath: options.ReportsRootPath,
        PoolPath: options.PoolPath,
        Execute: options.Execute,
        StatusOnly: options.Command == "status",
        SpreadMinutes: options.SpreadMinutes,
        OnlyAliases: options.OnlyAliases));

    return await sniper.RunAsync(CancellationToken.None);
}
catch (UsageException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    CliOptions.PrintUsage();
    return 2;
}
catch (SniperException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}
