namespace SpaceStroker.Seeder;

/// <summary>
/// One seeding pass over both target games.
///
/// A pass NEVER throws out: Program's loop must survive any single failure and try again at the
/// next tick. Each game is attempted independently, so a failure on the first cannot skip the
/// second.
/// </summary>
public sealed class SeedingPass
{
    /// <summary>
    /// Compile-time by decision (the card rejected a configurable list). Everything ELSE about
    /// these games — id, mint, decimals, stake, choice rules, presets — is resolved at runtime.
    /// </summary>
    public static readonly string[] TargetGames = { "Cosmic Sprint", "Beast Mode" };

    private readonly SeederConfig _config;
    private readonly SpaceStrokerApiClient _api;
    private readonly SolanaTransferClient _chain;
    private readonly BetSeeds _seeds;
    private readonly Random _random;

    public SeedingPass(
        SeederConfig config,
        SpaceStrokerApiClient api,
        SolanaTransferClient chain,
        BetSeeds seeds,
        Random random)
    {
        _config = config;
        _api = api;
        _chain = chain;
        _seeds = seeds;
        _random = random;
    }

    public static GameTypeDto? FindByName(IEnumerable<GameTypeDto> types, string name)
        => types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads the active game types. Used by every pass AND by the startup guard, which needs the
    /// token receiver before any money can move.
    ///
    /// There is no auth step in front of this any more. It used to mint a launch token first, but
    /// `remove-operators` deleted that endpoint — the 404 aborted every pass before it read anything.
    ///
    /// Returns NULL when the catalogue could not be READ at all (that skips the whole pass), and an
    /// EMPTY list when the API answered with no active types — a real answer, not a failure.
    /// </summary>
    public async Task<List<GameTypeDto>?> ResolveTargetsAsync(CancellationToken ct)
    {
        var types = await WithRetryAsync(
            () => _api.GetActiveTypesAsync(ct), SeederOperation.Discovery, RetryPhase.PreSend,
            "read the active game types", ct);

        if (!types.Ok || types.Value is null)
        {
            Log.Error($"could not read the active game types ({types.Error})");
            return null;
        }

        return types.Value;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        Log.Info("pass starting");

        var types = await ResolveTargetsAsync(ct);

        if (types is null)
        {
            Log.Warn("skipping this pass — the API could not be reached");
            return;
        }

        foreach (var name in TargetGames)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await SeedOneAsync(types, name, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Independent per game — a failure here must not skip the next target.
                Log.Error($"\"{name}\" failed unexpectedly", ex);
            }
        }

        Log.Info("pass finished");
    }

    private async Task SeedOneAsync(List<GameTypeDto> types, string name, CancellationToken ct)
    {
        var type = FindByName(types, name);

        ActiveGameDto? game = null;
        decimal? balance = null;

        if (type is not null)
        {
            var round = await WithRetryAsync(
                () => _api.GetActiveGameAsync(type.GameTypeId, ct),
                SeederOperation.Discovery, RetryPhase.PreSend, $"read the active round for \"{name}\"", ct);

            game = round.Value;

            // Only read the balance when the round could actually be seeded: it costs a real
            // upstream RPC call, and an already-seeded round never needs one.
            if (game is not null
                && GameStatus.IsAcceptingBets(game.Status)
                && (game.Bets?.Count ?? 0) == 0
                && !string.IsNullOrWhiteSpace(type.TokenMintAddress))
            {
                var read = await WithRetryAsync(
                    () => _api.GetTokenBalanceAsync(_config.WalletPublicKey, type.TokenMintAddress!, ct),
                    SeederOperation.Discovery, RetryPhase.PreSend, $"read the token balance for \"{name}\"", ct);

                balance = read.Ok ? read.Value?.UiAmount : null;
            }
        }

        var verdict = SeedDecision.Evaluate(name, type, game, balance);

        if (!verdict.ShouldSeed)
        {
            Log.Info($"skipping — {verdict.Reason}");
            return;
        }

        await PayAndBetAsync(type!, name, ct);
    }

    private async Task PayAndBetAsync(GameTypeDto type, string name, CancellationToken ct)
    {
        var choice = ChoicePicker.Pick(type, _random);

        Log.Info($"\"{name}\" — paying {type.RequiredAmount} for choice \"{choice}\"");

        var transfer = await _chain.SendStakeAsync(
            _config.Account, type.TokenMintAddress!, type.TokenReceiverPublicKey!,
            type.RequiredAmount, type.TokenDecimals!.Value, ct);

        if (!transfer.Ok || string.IsNullOrWhiteSpace(transfer.Signature))
        {
            // No signature exists, so nothing was spent — safe to skip until the next tick.
            Log.Error($"\"{name}\" — {transfer.Error}");
            return;
        }

        // ---- FROM HERE THE STAKE IS GONE. The transfer must never be re-sent. ----
        var signature = transfer.Signature!;
        Log.Info($"\"{name}\" — stake sent, signature {SolanaTransferClient.Short(signature)} — confirming");

        var confirmed = await _chain.ConfirmAsync(signature, TimeSpan.FromSeconds(_config.ConfirmTimeoutSeconds), ct);

        if (!confirmed)
        {
            Orphan(name, signature, type, choice, "the transfer never confirmed in time");
            return;
        }

        // Re-read the round: it may have rolled over while we were paying. A confirmed stake is
        // always spent if ANY round is accepting bets — the payment signature is not bound to a
        // gameId, and refusing here would strand tokens that already left the wallet. Emptiness
        // gates whether we PAY, not where an already-paid stake lands.
        var current = await WithRetryAsync(
            () => _api.GetActiveGameAsync(type.GameTypeId, ct),
            SeederOperation.PlaceBet, RetryPhase.PostSend, $"re-read the active round for \"{name}\"", ct);

        var target = current.Value;

        if (target is null || !GameStatus.IsAcceptingBets(target.Status))
        {
            Orphan(name, signature, type, choice,
                target is null ? "no round is accepting bets" : $"the round is {GameStatus.Name(target.Status)}");
            return;
        }

        var seed = _seeds.Next(out var nonce);

        var body = new RegisterBetBody(
            PaymentSignature: signature,
            Wallet: _config.WalletPublicKey,
            ExternalPlayerId: null,
            DisplayName: _config.DisplayName,
            Amount: type.RequiredAmount,
            Choice: choice,
            Seed: seed,
            Nonce: nonce);

        var placed = await WithRetryAsync(
            () => _api.PlaceBetAsync(target.GameId, body, ct),
            SeederOperation.PlaceBet, RetryPhase.PostSend, $"place the bet for \"{name}\"", ct);

        if (!placed.Ok)
        {
            Orphan(name, signature, type, choice, placed.Error);
            return;
        }

        Log.Info($"\"{name}\" — BET PLACED — round {target.GameId}, choice \"{choice}\", "
               + $"{type.RequiredAmount} staked, signature {SolanaTransferClient.Short(signature)}");
    }

    /// <summary>
    /// The stake confirmed on chain but no Bet row exists. Nothing auto-refunds this, so the FULL
    /// signature is logged (never shortened here) for manual reconciliation through the
    /// operator-admin POST /admin/bets/refund.
    /// </summary>
    private void Orphan(string name, string signature, GameTypeDto type, string choice, string why)
        => Log.Error($"ORPHAN PAYMENT — {why}. game=\"{name}\" wallet={_config.WalletPublicKey} "
                   + $"amount={type.RequiredAmount} choice=\"{choice}\" signature={signature} "
                   + "— reconcile by hand (POST /admin/bets/refund).");

    /// <summary>
    /// Bounded jittered retry, gated by the phase. Only TRANSIENT failures retry, and only when the
    /// phase permits the operation — RetryPhase.PostSend never allows SendTransfer.
    /// </summary>
    private async Task<ApiCall<T>> WithRetryAsync<T>(
        Func<Task<ApiCall<T>>> action,
        SeederOperation operation,
        RetryPhase phase,
        string what,
        CancellationToken ct)
    {
        var result = await action();

        if (result.Ok || !RetrySchedule.IsRetryable(phase, operation))
            return result;

        foreach (var delay in RetrySchedule.Delays(_config.RetryAttempts, _config.RetryBaseDelaySeconds, _random))
        {
            if (!RetrySchedule.IsTransient(result.Status))
                return result;

            Log.Warn($"could not {what} ({result.Error}) — retrying in {delay.TotalSeconds:F0}s");
            await Task.Delay(delay, ct);

            result = await action();

            if (result.Ok)
                return result;
        }

        return result;
    }
}
