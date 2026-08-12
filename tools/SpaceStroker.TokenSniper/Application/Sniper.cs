using System.Globalization;
using Solnet.Rpc.Models;
using Solnet.Wallet;
using SpaceStroker.TokenSniper.Domain;
using SpaceStroker.TokenSniper.Infrastructure;
using SpaceStroker.TokenSniper.Infrastructure.Reports;
using SpaceStroker.TokenSniper.Infrastructure.Solana;

namespace SpaceStroker.TokenSniper.Application;

public sealed record SniperOptions(
    string ReceiptsPath,
    string ReportsRootPath,
    string? PoolPath,
    bool Execute,
    bool StatusOnly,
    int? SpreadMinutes,
    IReadOnlyList<string> OnlyAliases);

/// <summary>
/// The run: load → read chain state → plan → preflight → confirm → buy → settle → report.
/// </summary>
public sealed class Sniper
{
    private readonly SecretsFile _secrets;
    private readonly SniperOptions _options;
    private readonly SolanaGateway _gateway;
    private readonly Random _rng;

    /// <summary>Read once per run from an accepted buy; the same value for every wallet on this mint.</summary>
    private PublicKey? _bondingCurveV2;

    public Sniper(SecretsFile secrets, SniperOptions options, SolanaGateway? gateway = null, Random? rng = null)
    {
        _secrets = secrets;
        _options = options;
        _gateway = gateway ?? new SolanaGateway(secrets.Solana.RpcUrl);
        _rng = rng ?? Random.Shared;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var pool = LoadPool();
        var mint = new PublicKey(_secrets.Snipe.Mint);

        Console.WriteLine($"rpc      : {_gateway.MaskedEndpoint}");
        Console.WriteLine($"mint     : {mint.Key}");
        Console.WriteLine($"pool     : {pool.Count} wallets");
        Console.WriteLine($"mode     : {(_options.StatusOnly ? "status (read-only)" : _options.Execute ? "EXECUTE" : "dry run")}");

        var mintInfo = await _gateway.GetMintInfoAsync(mint);
        var curveAddress = PumpFunProgram.DeriveBondingCurve(mint);
        var curve = await _gateway.GetCurveAsync(curveAddress);
        var global = await _gateway.GetGlobalAsync();

        Console.WriteLine($"supply   : {SnipePool.FormatTokens(mintInfo.TotalSupplyBaseUnits, mintInfo.Decimals)} " +
                          $"({mintInfo.Decimals} decimals, {ShortProgram(mintInfo.TokenProgram)})");
        Console.WriteLine($"fees     : {global.Fees.FeeBasisPoints} bps platform + {global.Fees.CreatorFeeBasisPoints} bps creator");

        // The two accounts the deployed program reads past the IDL's 16. See BuyTrace: the first is not
        // derivable from published seeds, so it is read off a buy the chain already accepted.
        _bondingCurveV2 = await _gateway.FindBondingCurveV2Async(mint);
        Console.WriteLine($"curve v2 : {_bondingCurveV2?.Key ?? "not found on any accepted buy"}");
        Console.WriteLine($"buyback  : {global.BuybackFeeRecipients.Count} fee recipients in global");

        var preflight = SnipePreflight.Check(pool, curve, mintInfo.TotalSupplyBaseUnits);

        // Blockers for BUYING specifically. status is read-only and structurally cannot submit, so it
        // reports these rather than aborting — unlike preflight's errors, which invalidate the plan itself.
        var buyBlockers = new List<string>();

        if (_bondingCurveV2 is null)
            buyBlockers.Add("No accepted buy on this mint carries the bonding_curve_v2 account, so it cannot be " +
                            "read and the program would reject every buy with BuybackFeeRecipientMissing. Wait " +
                            "for the creator's launch buy to confirm, then re-run.");

        if (global.BuybackFeeRecipients.Count == 0)
            buyBlockers.Add("The global account carries no buyback fee recipients — the pump.fun program layout " +
                            "changed. Re-read Global's field order from the on-chain IDL before buying.");

        foreach (var warning in preflight.Warnings) Console.WriteLine($"warning  : {warning}");
        if (_options.StatusOnly)
            foreach (var blocker in buyBlockers) Console.WriteLine($"warning  : {blocker}");

        if (preflight.IsEmpty)
        {
            Console.WriteLine("The wallet pool is empty — nothing to do.");
            return 0;
        }

        var errors = _options.StatusOnly ? preflight.Errors : [.. preflight.Errors, .. buyBlockers];

        if (errors.Count > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Preflight failed — nothing was submitted:");
            foreach (var error in errors) Console.Error.WriteLine($"  - {error}");
            return 1;
        }

        // curve is non-null here: preflight errors on a missing one.
        var state = curve!;
        var targets = await BuildTargetsAsync(pool, mint, mintInfo, state);
        var funding = await SnapshotFundingAsync(pool, mint, mintInfo);

        var plan = SnipePlan.Build(targets, funding, state, global.Fees,
            _secrets.Snipe.SlippageTolerance, _secrets.Snipe.ReserveLamports);

        var window = TimeSpan.FromMinutes(_options.SpreadMinutes ?? _secrets.Snipe.SpreadMinutes);
        var schedule = BuySchedule.Build(plan, window, _rng);

        Console.WriteLine(ConsoleReport.RenderLadder(schedule, mintInfo.Decimals));

        var report = NewReport(mint, mintInfo, schedule, dryRun: !_options.Execute || _options.StatusOnly);
        var ledger = new RunLedger(report);
        ledger.Recalculate();

        if (_options.StatusOnly)
        {
            Console.WriteLine(ConsoleReport.RenderSummary(report));
            return 0;
        }

        if (!_options.Execute)
        {
            Console.WriteLine("Dry run — nothing submitted. Re-run with --execute to buy.");
            return 0;
        }

        if (!Confirm(schedule)) return 1;

        var receipts = new ReceiptsStore(_options.ReceiptsPath, report);
        receipts.Save();

        await ExecuteAsync(schedule, mint, mintInfo, global, state, ledger, receipts, window, cancellationToken);

        report.FinishedUtc = DateTimeOffset.UtcNow;
        ledger.Recalculate();
        receipts.Save();

        var directory = Path.Combine(_options.ReportsRootPath,
            report.StartedUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));

        var json = JsonReportWriter.Write(report, directory);
        var html = HtmlReportWriter.Write(report, directory);

        Console.WriteLine(ConsoleReport.RenderSummary(report));
        Console.WriteLine($"  reports: {json}");
        Console.WriteLine($"           {html}");

        return ledger.ExitCode;
    }

    private async Task ExecuteAsync(
        IReadOnlyList<ScheduledBuy> schedule,
        PublicKey mint,
        MintInfo mintInfo,
        GlobalConfig global,
        CurveState openingState,
        RunLedger ledger,
        ReceiptsStore receipts,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var curveAddress = PumpFunProgram.DeriveBondingCurve(mint);
        var walletIndex = 0;

        foreach (var scheduled in schedule)
        {
            var outcome = ledger.For(scheduled.Buy.Target.Alias);

            if (scheduled.Buy.IsSkip)
            {
                outcome.Status = WalletStatus.Skipped;
                receipts.Save();
                continue;
            }

            var wait = started + scheduled.Offset - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                Console.WriteLine($"  waiting {wait:hh\\:mm\\:ss} before {scheduled.Buy.Target.Alias}...");
                await Task.Delay(wait, cancellationToken);
            }

            // Re-read the curve: our own earlier buys and everyone else's have moved the price, and a
            // graduation between wallets must stop the run rather than fail obscurely.
            var live = await _gateway.GetCurveAsync(curveAddress);
            if (live is null || BondingCurve.IsGraduated(live))
            {
                ledger.Report.StoppedReason =
                    $"the token graduated from the bonding curve after {LastFilled(ledger)} — " +
                    $"{schedule.Count(s => ledger.For(s.Buy.Target.Alias).Status is WalletStatus.Pending)} wallets left unbought";

                foreach (var remaining in schedule)
                {
                    var pending = ledger.For(remaining.Buy.Target.Alias);
                    if (pending.Status == WalletStatus.Pending)
                    {
                        pending.Status = WalletStatus.Skipped;
                        pending.Note = "curve graduated before this wallet executed";
                    }
                }

                receipts.Save();
                Console.Error.WriteLine($"  !! {ledger.Report.StoppedReason}");
                return;
            }

            // Any of global's 8 buyback recipients is accepted; rotating spreads the writes across them
            // instead of every wallet in the run contending on the same account.
            var recipient = new PublicKey(
                global.BuybackFeeRecipients[walletIndex++ % global.BuybackFeeRecipients.Count]);

            await BuyOneAsync(scheduled, live, mint, mintInfo, global, recipient, outcome, cancellationToken);
            receipts.Save();
        }
    }

    private async Task BuyOneAsync(
        ScheduledBuy scheduled,
        CurveState live,
        PublicKey mint,
        MintInfo mintInfo,
        GlobalConfig global,
        PublicKey buybackFeeRecipient,
        WalletOutcome outcome,
        CancellationToken cancellationToken)
    {
        var target = scheduled.Buy.Target;
        var wallet = new PublicKey(target.PublicKey);
        var entry = LoadPool().First(e => e.Alias == target.Alias);
        var signer = new Account(entry.PrivateKey, entry.PublicKey);

        var ata = PumpFunProgram.DeriveAssociatedTokenAccount(wallet, mint, mintInfo.TokenProgram);
        var heldBefore = await _gateway.GetTokenBalanceAsync(ata);

        // The RESUME AUTHORITY: buy only the shortfall the chain actually shows.
        var shortfall = target.TargetBaseUnits > heldBefore ? target.TargetBaseUnits - heldBefore : 0;
        if (shortfall == 0)
        {
            outcome.Status = WalletStatus.Skipped;
            outcome.HeldBeforeBaseUnits = heldBefore;
            outcome.HeldAfterBaseUnits = heldBefore;
            outcome.Note = "already at target on-chain";
            return;
        }

        var lamports = await _gateway.GetLamportBalanceAsync(wallet);
        var ataExists = await _gateway.AccountExistsAsync(ata);
        var funding = new Dictionary<string, WalletFunding>
        {
            [target.Alias] = new(lamports, !ataExists),
        };

        var repriced = SnipePlan.Build(
            [target with { HeldBaseUnits = heldBefore }], funding, live, global.Fees,
            _secrets.Snipe.SlippageTolerance, _secrets.Snipe.ReserveLamports).Single();

        if (repriced.IsSkip)
        {
            outcome.Status = WalletStatus.Short;
            outcome.HeldBeforeBaseUnits = heldBefore;
            outcome.HeldAfterBaseUnits = heldBefore;
            outcome.Note = repriced.SkipReason ?? "nothing affordable at the live price";
            return;
        }

        var instructions = new List<TransactionInstruction>
        {
            PumpFunProgram.SetComputeUnitLimit(_secrets.Snipe.ComputeUnitLimit),
            PumpFunProgram.SetComputeUnitPrice(_secrets.Snipe.PriorityFeeMicroLamports),
        };

        if (!ataExists)
            instructions.Add(PumpFunProgram.BuildCreateAssociatedTokenAccount(
                wallet, wallet, mint, mintInfo.TokenProgram));

        instructions.Add(PumpFunProgram.BuildBuyInstruction(
            new PumpFunProgram.BuyAccounts(
                FeeRecipient: new PublicKey(global.FeeRecipient),
                Mint: mint,
                BondingCurve: PumpFunProgram.DeriveBondingCurve(mint),
                AssociatedBondingCurve: PumpFunProgram.DeriveAssociatedTokenAccount(
                    PumpFunProgram.DeriveBondingCurve(mint), mint, mintInfo.TokenProgram),
                AssociatedUser: ata,
                User: wallet,
                TokenProgram: mintInfo.TokenProgram,
                CreatorVault: PumpFunProgram.DeriveCreatorVault(new PublicKey(live.Creator))),
            repriced.TokensToBuy,
            repriced.MaxSolCostLamports,
            _secrets.Snipe.TrackVolume,
            _secrets.Snipe.ExtraAccounts.Select(a => new PublicKey(a)),
            _bondingCurveV2,
            buybackFeeRecipient));

        Console.WriteLine($"  {target.Alias}: buying {SnipePool.FormatTokens(repriced.TokensToBuy, mintInfo.Decimals)} " +
                          $"for up to {SnipePool.FormatSol(repriced.MaxSolCostLamports)} SOL...");

        var submitted = await _gateway.SubmitAsync(instructions, signer);
        outcome.HeldBeforeBaseUnits = heldBefore;
        outcome.TimestampUtc = DateTimeOffset.UtcNow;

        if (submitted.Signature is null)
        {
            outcome.Status = WalletStatus.Failed;
            outcome.HeldAfterBaseUnits = heldBefore;
            outcome.Note = $"submit failed: {submitted.Error}";
            Console.Error.WriteLine($"  {target.Alias}: {outcome.Note}");
            return;
        }

        outcome.Signature = submitted.Signature;

        await _gateway.ConfirmAsync(
            submitted.Signature,
            TimeSpan.FromSeconds(_secrets.Snipe.ConfirmTimeoutSeconds),
            TimeSpan.FromSeconds(Math.Max(1, _secrets.Snipe.BalancePollSeconds)),
            cancellationToken);

        // ⚠️ The signature's status is NOT the verdict — an observed balance increase is. This is also
        // what makes a retry safe: the balance says whether the buy landed, so nothing is re-submitted
        // on top of a transaction that already succeeded.
        var heldAfter = await _gateway.GetTokenBalanceAsync(ata);
        var received = heldAfter > heldBefore ? heldAfter - heldBefore : 0;

        outcome.HeldAfterBaseUnits = heldAfter;
        outcome.TokensReceivedBaseUnits = received;
        outcome.SolSpentLamports = received == 0 ? 0 : repriced.QuotedCostLamports;

        if (received == 0)
        {
            outcome.Status = WalletStatus.Unconfirmed;
            outcome.Note = "submitted, but no token-balance increase was observed before the timeout";
        }
        else if (heldAfter >= target.TargetBaseUnits)
        {
            outcome.Status = WalletStatus.Filled;
        }
        else
        {
            outcome.Status = WalletStatus.Short;
            outcome.Note = repriced.CappedByBalance
                ? "bought the maximum the wallet's SOL allowed"
                : "landed below target";
        }
    }

    private bool Confirm(IReadOnlyList<ScheduledBuy> schedule)
    {
        var expected = ConsoleReport.ConfirmationTotal(schedule);
        Console.WriteLine();
        Console.WriteLine($"This will spend up to the quoted total with real SOL and cannot be undone.");
        Console.Write($"Type the quoted total ({expected}) to continue: ");

        var typed = Console.ReadLine();
        if (ConsoleReport.ConfirmationMatches(expected, typed)) return true;

        Console.Error.WriteLine("The typed total did not match exactly — nothing was submitted.");
        return false;
    }

    private RunReport NewReport(PublicKey mint, MintInfo mintInfo, IReadOnlyList<ScheduledBuy> schedule, bool dryRun)
    {
        var report = new RunReport
        {
            Mint = mint.Key,
            DryRun = dryRun,
            StartedUtc = DateTimeOffset.UtcNow,
            MintTotalSupplyBaseUnits = mintInfo.TotalSupplyBaseUnits,
            TokenDecimals = mintInfo.Decimals,
        };

        foreach (var scheduled in schedule)
            report.Wallets.Add(new WalletOutcome
            {
                Alias = scheduled.Buy.Target.Alias,
                PublicKey = scheduled.Buy.Target.PublicKey,
                TargetPercent = scheduled.Buy.Target.SupplyPercentage,
                TargetBaseUnits = scheduled.Buy.Target.TargetBaseUnits,
                HeldBeforeBaseUnits = scheduled.Buy.Target.HeldBaseUnits,
                HeldAfterBaseUnits = scheduled.Buy.Target.HeldBaseUnits,
                ScheduledOffsetSeconds = scheduled.Offset.TotalSeconds,
                Status = scheduled.Buy.IsSkip ? WalletStatus.Skipped : WalletStatus.Pending,
                Note = scheduled.Buy.SkipReason,
            });

        return report;
    }

    private async Task<IReadOnlyList<WalletTarget>> BuildTargetsAsync(
        IReadOnlyList<PoolEntry> pool, PublicKey mint, MintInfo mintInfo, CurveState curve)
    {
        var targets = new List<WalletTarget>(pool.Count);

        foreach (var entry in pool)
        {
            var wallet = new PublicKey(entry.PublicKey);
            var ata = PumpFunProgram.DeriveAssociatedTokenAccount(wallet, mint, mintInfo.TokenProgram);
            var held = await _gateway.GetTokenBalanceAsync(ata);

            targets.Add(new WalletTarget(
                entry.Alias,
                entry.PublicKey,
                entry.SupplyPercentage,
                SnipePlan.TargetFor(entry.SupplyPercentage, mintInfo.TotalSupplyBaseUnits),
                held));
        }

        return targets.OrderBy(t => t.Alias, StringComparer.Ordinal).ToList();
    }

    private async Task<IReadOnlyDictionary<string, WalletFunding>> SnapshotFundingAsync(
        IReadOnlyList<PoolEntry> pool, PublicKey mint, MintInfo mintInfo)
    {
        var funding = new Dictionary<string, WalletFunding>(pool.Count);

        foreach (var entry in pool)
        {
            var wallet = new PublicKey(entry.PublicKey);
            var ata = PumpFunProgram.DeriveAssociatedTokenAccount(wallet, mint, mintInfo.TokenProgram);

            funding[entry.Alias] = new WalletFunding(
                await _gateway.GetLamportBalanceAsync(wallet),
                !await _gateway.AccountExistsAsync(ata));
        }

        return funding;
    }

    private IReadOnlyList<PoolEntry> LoadPool()
    {
        var pool = _options.PoolPath is null
            ? SnipePool.FromEntries(_secrets.Wallets)
            : SnipePool.Parse(File.ReadAllText(_options.PoolPath));

        if (_options.OnlyAliases.Count > 0)
            pool = pool.Where(e => _options.OnlyAliases.Contains(e.Alias, StringComparer.Ordinal)).ToList();

        return pool;
    }

    private static string LastFilled(RunLedger ledger) =>
        ledger.Report.Wallets.LastOrDefault(w => w.Status == WalletStatus.Filled)?.Alias ?? "no wallets";

    private static string ShortProgram(PublicKey tokenProgram) =>
        tokenProgram.Key == PumpFunProgram.Token2022ProgramId.Key ? "Token-2022" : "SPL Token";
}
