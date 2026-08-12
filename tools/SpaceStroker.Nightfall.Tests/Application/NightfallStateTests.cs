using Shouldly;
using SpaceStroker.Nightfall.Application;
using SpaceStroker.Nightfall.Domain;
using SpaceStroker.Nightfall.Infrastructure.Solana;
using Xunit;

namespace SpaceStroker.Nightfall.Tests.Application;

public class NightfallStateTests
{
    private const string Wallet = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";
    private const string Other = "ATokenGPvbdGVxr1b2hvZbsiqW5xWH25efTNsLJA8knL";

    private static NightfallState State(params string[] wallets)
    {
        var pool = wallets.Select((w, i) => new PoolEntry($"W{i + 1:00}", w, "key")).ToList();

        return new NightfallState(pool, new SecretsView(
            Mint: "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v",
            MinSolForFeeSol: 0.003m,
            SlippageBps: 300,
            MaxPriceImpactPercent: 10m,
            MaxConcurrentSells: 4))
        {
            Mint = new MintInfo(1_000_000_000UL, 6),
        };
    }

    [Fact]
    public void Starts_in_simulate_so_a_fresh_process_is_never_armed()
    {
        // AC13. The mode is never persisted — arming is always a deliberate act in this session.
        State(Wallet).IsLive.ShouldBeFalse();
    }

    [Fact]
    public void A_wallet_already_in_flight_cannot_be_claimed_again()
    {
        // AC22. The browser can double-fire, and a second sell is a real second sale.
        var state = State(Wallet);

        state.TryBeginSell(Wallet).ShouldBeTrue();
        state.TryBeginSell(Wallet).ShouldBeFalse();
    }

    [Fact]
    public void Releasing_a_wallet_makes_it_claimable_again()
    {
        // A failed sell must leave the wallet re-selectable, not stuck.
        var state = State(Wallet);
        state.TryBeginSell(Wallet);

        state.EndSell(Wallet);

        state.TryBeginSell(Wallet).ShouldBeTrue();
    }

    [Fact]
    public void A_poll_does_not_overwrite_a_wallet_that_is_mid_sell()
    {
        // Otherwise the row flickers between "selling" and a stale balance at exactly the moment the
        // operator is watching it most closely.
        var state = State(Wallet, Other);
        state.ApplyBalances(new Dictionary<string, WalletBalances>
        {
            [Wallet] = new(1_000_000_000L, 500UL),
            [Other] = new(1_000_000_000L, 500UL),
        });

        state.TryBeginSell(Wallet);

        state.ApplyBalances(new Dictionary<string, WalletBalances>
        {
            [Wallet] = new(9L, 9UL),
            [Other] = new(9L, 9UL),
        });

        state.BalanceOf(Wallet).TokenRaw.ShouldBe(500UL);   // held
        state.BalanceOf(Other).TokenRaw.ShouldBe(9UL);      // updated
    }

    [Fact]
    public void An_immediate_post_sell_refresh_bypasses_the_in_flight_hold()
    {
        var state = State(Wallet);
        state.TryBeginSell(Wallet);

        state.ApplyBalance(Wallet, new WalletBalances(5L, 0UL));

        state.BalanceOf(Wallet).TokenRaw.ShouldBe(0UL);
    }

    [Fact]
    public void The_snapshot_reports_supply_share_and_marks_a_zero_balance_wallet_unselectable()
    {
        var state = State(Wallet);
        state.ApplyBalances(new Dictionary<string, WalletBalances> { [Wallet] = new(1_000_000_000L, 20_000_000UL) });

        var view = state.Snapshot().Wallets.Single();

        view.SupplyPercent.ShouldBe(2m);           // 20,000,000 of 1,000,000,000
        view.TokenAmount.ShouldBe(20m);            // 6 decimals
        view.Selectable.ShouldBeTrue();
    }

    [Fact]
    public void A_wallet_that_cannot_pay_the_fee_is_unselectable_with_a_reason()
    {
        // AC16 — surfaced on the card face, before the operator spends a confirmation on it.
        var state = State(Wallet);
        state.ApplyBalances(new Dictionary<string, WalletBalances> { [Wallet] = new(1_000L, 500UL) });

        var view = state.Snapshot().Wallets.Single();

        view.Selectable.ShouldBeFalse();
        view.Reason.ShouldContain("spacestroker-fund-wallets");
    }

    [Fact]
    public void A_wallet_holding_none_of_the_token_is_unselectable()
    {
        // AC17.
        var state = State(Wallet);
        state.ApplyBalances(new Dictionary<string, WalletBalances> { [Wallet] = new(1_000_000_000L, 0UL) });

        state.Snapshot().Wallets.Single().Selectable.ShouldBeFalse();
    }

    [Fact]
    public void The_snapshot_served_to_the_browser_carries_no_private_key()
    {
        // AC27, structurally: WalletView has no key member, so the serializer cannot leak one.
        var state = State(Wallet);
        state.ApplyBalances(new Dictionary<string, WalletBalances> { [Wallet] = new(1_000_000_000L, 500UL) });

        var json = System.Text.Json.JsonSerializer.Serialize(state.Snapshot());

        json.ToLowerInvariant().ShouldNotContain("privatekey");
        json.ShouldNotContain("\"key\"");
    }
}
