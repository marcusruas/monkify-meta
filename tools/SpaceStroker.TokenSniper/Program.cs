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
