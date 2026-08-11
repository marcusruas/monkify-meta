using SpaceStroker.WalletFunder.Application;
using SpaceStroker.WalletFunder.Domain;
using SpaceStroker.WalletFunder.Infrastructure;
using SpaceStroker.WalletFunder.Infrastructure.Coinbase;

// spacestroker-fund-wallets — funds a pool of Solana wallets with SOL from a Coinbase account.
// Usage:
//   spacestroker-fund-wallets fund              build the plan; with --execute, fund the pool
//   spacestroker-fund-wallets status            read-only: poll pending sends + re-check on-chain. Never sends.
// Options:
//   --secrets <path>       secrets file (default: tools/secrets.json)
//   --pool <path>          load the wallet array from a standalone JSON file instead of secrets.wallets
//   --receipts <path>      receipts file (default: tools/receipts.json)
//   --execute              actually send. Without it, `fund` is a dry run.
//   --spread-minutes <n>   override funding.spreadMinutes for this run
//   --force-plan-change    resume against receipts whose plan hash differs (DANGEROUS — can double-fund)

try
{
    var opts = CliOptions.Parse(args);
    if (opts.ShowHelp)
    {
        CliOptions.PrintUsage();
        return 0;
    }

    var secrets = SecretsFile.Load(opts.SecretsPath);
    var funder = new Funder(secrets, new FunderOptions(
        ReceiptsPath: opts.ReceiptsPath,
        PoolPath: opts.PoolPath,
        Execute: opts.Execute,
        StatusOnly: opts.Command == "status",
        ForcePlanChange: opts.ForcePlanChange,
        SpreadMinutes: opts.SpreadMinutes));

    return await funder.RunAsync(CancellationToken.None);
}
catch (CoinbaseAuthException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}
catch (FunderException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}
catch (UsageException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    CliOptions.PrintUsage();
    return 2;
}
