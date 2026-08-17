using System.Collections.Concurrent;
using SpaceStroker.Nightfall.Domain;
using SpaceStroker.Nightfall.Infrastructure.Solana;

namespace SpaceStroker.Nightfall.Application;

/// <summary>
/// What the browser is allowed to know about a wallet.
///
/// ⚠️ There is deliberately NO private-key member on this type. That is what makes "no key ever
/// reaches the browser" structural rather than a review item — the serializer cannot leak a field
/// that does not exist.
/// </summary>
public sealed record WalletView(
    string Alias,
    string PublicKey,
    decimal Sol,
    string TokenAmountRaw,
    decimal TokenAmount,
    decimal SupplyPercent,
    decimal EstimatedSol,
    string Status,
    string? Reason,
    decimal? QuotedSol,
    decimal? RealizedSol,
    string? Signature,
    bool Selectable);

public sealed record StateView(
    bool Live,
    string TokenMint,
    int Decimals,
    string TotalSupply,
    decimal UnitPriceSol,
    decimal SlippagePercent,
    decimal MaxPriceImpactPercent,
    int MaxConcurrentSells,
    DateTimeOffset? RefreshedUtc,
    IReadOnlyList<WalletView> Wallets);

/// <summary>
/// The single in-memory picture of the pool, plus the SIMULATE/LIVE switch and the in-flight set.
/// </summary>
public sealed class NightfallState(IReadOnlyList<PoolEntry> pool, SecretsView settings)
{
    private readonly ConcurrentDictionary<string, WalletBalances> _balances = new();
    private readonly ConcurrentDictionary<string, SellReceipt> _receipts = new();
    private readonly ConcurrentDictionary<string, byte> _inFlight = new();

    public IReadOnlyList<PoolEntry> Pool { get; } = pool;

    /// <summary>
    /// SIMULATE at every launch, never persisted. A fresh process is never armed — the operator has
    /// to arm it deliberately, which is the whole point of the default.
    /// </summary>
    public bool IsLive { get; private set; }

    public MintInfo Mint { get; set; } = new(0UL, 0, SwapInstructionsGuard.TokenProgramId);
    public decimal UnitPriceSol { get; set; }
    public DateTimeOffset? RefreshedUtc { get; private set; }

    public void SetLive(bool live) => IsLive = live;

    public PoolEntry? Find(string publicKey) => Pool.FirstOrDefault(p => p.PublicKey == publicKey);

    /// <summary>Claims a wallet for a sell. False means it is already in flight — never sell twice.</summary>
    public bool TryBeginSell(string publicKey) => _inFlight.TryAdd(publicKey, 0);

    public void EndSell(string publicKey) => _inFlight.TryRemove(publicKey, out _);

    public bool IsInFlight(string publicKey) => _inFlight.ContainsKey(publicKey);

    public WalletBalances BalanceOf(string publicKey)
        => _balances.TryGetValue(publicKey, out var value) ? value : new WalletBalances(0L, 0UL);

    public void ApplyBalances(IReadOnlyDictionary<string, WalletBalances> balances)
    {
        foreach (var (key, value) in balances)
        {
            // A wallet mid-sell keeps its live row: a poll landing between broadcast and settlement
            // would otherwise flicker the number the operator is watching most closely.
            if (_inFlight.ContainsKey(key)) continue;

            _balances[key] = value;
        }

        RefreshedUtc = DateTimeOffset.UtcNow;
    }

    public void ApplyBalance(string publicKey, WalletBalances balance)
    {
        _balances[publicKey] = balance;
        RefreshedUtc = DateTimeOffset.UtcNow;
    }

    public void Record(SellReceipt receipt) => _receipts[receipt.PublicKey] = receipt;

    public SellReceipt? ReceiptFor(string publicKey)
        => _receipts.TryGetValue(publicKey, out var receipt) ? receipt : null;

    public StateView Snapshot()
    {
        var wallets = Pool.Select(entry =>
        {
            var balance = BalanceOf(entry.PublicKey);
            var receipt = ReceiptFor(entry.PublicKey);
            var exclusion = SellPreflight.Check(balance.TokenRaw, balance.SolLamports, settings.MinSolForFeeSol);
            var tokens = TokenAmount.ToUi(balance.TokenRaw, Mint.Decimals);

            return new WalletView(
                Alias: entry.Alias,
                PublicKey: entry.PublicKey,
                Sol: TokenAmount.LamportsToSol(balance.SolLamports),
                TokenAmountRaw: balance.TokenRaw.ToString(),
                TokenAmount: tokens,
                SupplyPercent: Mint.Supply == 0UL ? 0m : balance.TokenRaw / (decimal)Mint.Supply * 100m,
                EstimatedSol: tokens * UnitPriceSol,
                Status: _inFlight.ContainsKey(entry.PublicKey) ? "Selling" : receipt?.Status.ToString() ?? "Idle",
                Reason: exclusion == SellExclusion.None ? receipt?.Error : SellPreflight.Describe(exclusion),
                QuotedSol: receipt?.QuotedSol,
                RealizedSol: receipt?.RealizedSol,
                Signature: receipt?.Signature,
                Selectable: exclusion == SellExclusion.None);
        }).ToList();

        return new StateView(
            Live: IsLive,
            TokenMint: settings.Mint,
            Decimals: Mint.Decimals,
            TotalSupply: Mint.Supply.ToString(),
            UnitPriceSol: UnitPriceSol,
            SlippagePercent: settings.SlippageBps / 100m,
            MaxPriceImpactPercent: settings.MaxPriceImpactPercent,
            MaxConcurrentSells: settings.MaxConcurrentSells,
            RefreshedUtc: RefreshedUtc,
            Wallets: wallets);
    }
}

/// <summary>The settings the state needs, lifted out of SecretsFile so the state stays testable.</summary>
public sealed record SecretsView(
    string Mint,
    decimal MinSolForFeeSol,
    int SlippageBps,
    decimal MaxPriceImpactPercent,
    int MaxConcurrentSells);
