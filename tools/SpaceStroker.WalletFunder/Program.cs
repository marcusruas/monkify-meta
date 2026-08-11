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
//   --receipts <path>      receipts file (default: tools/Receipts/receipts.json)
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

    // ⚠️ Card #167 moved the default receipts path into tools/Receipts/. A pre-refactor file the tool
    // cannot see reads as "no prior run", and this tool's Coinbase idem is derived from the plan — so
    // starting fresh here would issue REAL second transfers. Refuse rather than risk it.
    var legacyReceiptsPath = Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")), "receipts.json");
    var legacyReceipts = LegacyReceipts.Describe(
        File.Exists(opts.ReceiptsPath), File.Exists(legacyReceiptsPath), opts.ReceiptsPath, legacyReceiptsPath);

    if (legacyReceipts is not null)
        throw new FunderException(legacyReceipts);

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
