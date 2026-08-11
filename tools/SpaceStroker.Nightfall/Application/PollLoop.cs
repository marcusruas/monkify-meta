using SpaceStroker.Nightfall.Domain;
using SpaceStroker.Nightfall.Infrastructure;
using SpaceStroker.Nightfall.Infrastructure.Jupiter;
using SpaceStroker.Nightfall.Infrastructure.Solana;

namespace SpaceStroker.Nightfall.Application;

/// <summary>
/// Keeps the dashboard honest without the operator clicking anything.
/// </summary>
public sealed class PollLoop(
    NightfallState state,
    SecretsFile secrets,
    SolanaReader reader,
    JupiterClient jupiter,
    Action<string> log)
{
    public async Task RefreshAsync(CancellationToken ct)
    {
        var balances = await reader.GetBalancesAsync(state.Pool, ct);
        state.ApplyBalances(balances);

        await RefreshUnitPriceAsync(ct);
    }

    /// <summary>
    /// ONE quote per cycle for the whole pool, against a reference amount — not one per wallet.
    ///
    /// ⚠️ Load-bearing, not a micro-optimisation: Jupiter's free tier would be exhausted within
    /// minutes by a per-wallet quote every 15 seconds across a 20-wallet pool, and the dashboard
    /// would show errors instead of prices. The card face is explicitly an ESTIMATE because of this
    /// (it ignores per-wallet price impact); the hover quote and the confirmation dialog use real
    /// per-wallet quotes.
    /// </summary>
    private async Task RefreshUnitPriceAsync(CancellationToken ct)
    {
        var holdings = state.Pool
            .Select(p => state.BalanceOf(p.PublicKey).TokenRaw)
            .Where(raw => raw > 0UL)
            .OrderBy(raw => raw)
            .ToList();

        if (holdings.Count == 0)
        {
            state.UnitPriceSol = 0m;
            return;
        }

        // The median holding is the reference: a quote for a typical position prices the pool far
        // better than one for its largest or smallest.
        var reference = holdings[holdings.Count / 2];

        try
        {
            var quote = await jupiter.QuoteAsync(secrets.Token.Mint, reference, ct);
            var referenceTokens = TokenAmount.ToUi(reference, state.Mint.Decimals);

            state.UnitPriceSol = referenceTokens == 0m ? 0m : quote.OutSol / referenceTokens;
        }
        catch (Exception ex)
        {
            // A missing price is cosmetic — balances and percentages stay correct, and the estimate
            // column simply reads zero until the next cycle.
            log($"Could not price the token this cycle: {ex.Message}");
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(3, secrets.Sell.RefreshIntervalSeconds));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);
                await RefreshAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                log($"Refresh failed: {ex.Message}");
            }
        }
    }
}
