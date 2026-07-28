using SpaceStroker.Seeder;

// spacestroker-seeder — places one real paid bet into Cosmic Sprint and Beast Mode whenever their
// current round is empty, on start and then every intervalHours.
//
// Local operator tool. It talks only to endpoints that already ship and changes nothing in the
// product. See monkify-meta/specs/auto-bet-seeder.md.

var configPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "secrets.seeder.json");

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;             // unwind the loop cleanly instead of killing the process mid-transfer
    Log.Info("stopping — finishing the current step");
    cts.Cancel();
};

try
{
    var config = SeederConfig.Load(Path.GetFullPath(configPath));

    using var api = new SpaceStrokerApiClient(config.ApiBaseUrl);
    var chain = new SolanaTransferClient($"{config.ApiBaseUrl}/rpc");
    var pass = new SeedingPass(config, api, chain, new BetSeeds(), new Random());

    Log.Info("spacestroker-seeder starting");
    Log.Info($"  api        {config.ApiBaseUrl}");
    Log.Info($"  wallet     {config.WalletPublicKey}");
    Log.Info($"  name       {config.DisplayName}");
    Log.Info($"  interval   every {config.IntervalHours}h");
    Log.Info($"  targets    {string.Join(", ", SeedingPass.TargetGames)}");

    await GuardAgainstTheTreasuryWalletAsync(pass, config, cts.Token);

    while (!cts.IsCancellationRequested)
    {
        try
        {
            await pass.RunAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            break;
        }
        catch (Exception ex)
        {
            // A failed pass never kills the loop — the next tick tries again.
            Log.Error("the pass failed unexpectedly", ex);
        }

        Log.Info($"next pass in {config.IntervalHours}h");

        try
        {
            await Task.Delay(TimeSpan.FromHours(config.IntervalHours), cts.Token);
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }

    Log.Info("stopped");
    return 0;
}
catch (SeederException ex)
{
    Log.Error(ex.Message);
    return 1;
}
catch (OperationCanceledException)
{
    Log.Info("stopped");
    return 0;
}

// The one guard that must run before any money can move.
//
// SolanaService.ValidateBetPayment matches a transfer's pre/post token balances BY OWNER — the
// token owner on the recipient side, the bet's wallet on the sender side. If those are the same
// wallet, both lookups resolve to the SAME balance entry, the delta is 0, and the bet is rejected
// as underpaid AFTER the transfer has already confirmed. The transfer itself is a self-transfer
// no-op, so without this guard the tool would burn fees and fail silently every 6 hours forever.
static async Task GuardAgainstTheTreasuryWalletAsync(SeedingPass pass, SeederConfig config, CancellationToken ct)
{
    var (launchToken, types) = await pass.ResolveTargetsAsync(ct);

    if (launchToken is null || types.Count == 0)
    {
        Log.Warn("could not verify the seeder wallet against the operator's receiving wallet "
               + "(the API was unreachable at startup) — the guard has NOT run for this session");
        return;
    }

    foreach (var name in SeedingPass.TargetGames)
    {
        var type = SeedingPass.FindByName(types, name);

        if (type?.TokenReceiverPublicKey is null)
            continue;

        if (string.Equals(type.TokenReceiverPublicKey, config.WalletPublicKey, StringComparison.Ordinal))
        {
            throw new SeederException(
                $"The configured wallet ({config.WalletPublicKey}) IS the operator's receiving wallet for \"{name}\". "
              + "A transfer from that wallet to itself confirms as a no-op, so every bet would be rejected as "
              + "underpaid after the money moved. Configure a separate wallet.");
        }
    }

    Log.Info("  guard      OK — the seeder wallet is not the operator's receiving wallet");
}
