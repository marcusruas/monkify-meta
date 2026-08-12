using Shouldly;
using SpaceStroker.TokenSniper.Infrastructure.Solana;
using Xunit;

namespace SpaceStroker.TokenSniper.Tests.Infrastructure;

/// <summary>
/// The deployed pump.fun program reads two accounts past the 16 its published IDL declares. The first
/// of them (<c>bonding_curve_v2</c>) is a per-mint PDA whose seeds are NOT published — every const seed
/// in the on-chain IDL was tried against four live buys and none reproduce it. So the tool does what
/// the rest of this file already does for the account list: it reads the value off a real transaction.
///
/// Fixture: the creator's own dev buy, which every pump.fun token has by construction — signature
/// 3YjPNE5f8KJAfpWURk2YqAazsFgP4wirnyJ4bEttv2Fdd21tmLE6nc9Yxxq2zhwGKg2EgX99zM7EmAL7goXFgay1
/// (mainnet, captured 2026-08-12), 18 accounts.
/// </summary>
public class BuyTraceTests
{
    private const string PumpProgram = "6EF8rrecthR5Dkzon8Nwu78hRvfCKubJ14M5uBEwF6P";
    private const string BondingCurveV2 = "4Twez7HGGNc9ef4HpddGWNE3GWk9w8eygtvkpmU1gVCU";
    private const string BuybackFeeRecipient = "GXPFM2caqTtQYC2cJ5yJRi9VDkpsYZXzYdwYpGnLmtDL";

    /// <summary>Account keys 0..17, with the two trailing accounts at 16 and 17.</summary>
    private static List<string> Keys() =>
    [
        .. Enumerable.Range(0, 16).Select(i => $"key{i:00}"),
        BondingCurveV2,
        BuybackFeeRecipient,
    ];

    private static byte[] BuyData() => [.. PumpFunProgram.BuyDiscriminator, .. new byte[16]];

    private static TraceInstruction Buy(int accountCount = 18) =>
        new(ProgramIdIndex: 18, [.. Enumerable.Range(0, accountCount)], BuyData());

    [Fact]
    public void FindBondingCurveV2_ReadsAccountSixteenOfALivePumpBuy()
    {
        var keys = Keys();
        keys.Add(PumpProgram);   // index 18, the program itself

        BuyTrace.FindBondingCurveV2(keys, [Buy()])!.Key.ShouldBe(BondingCurveV2);
    }

    [Fact]
    public void FindBondingCurveV2_IgnoresInstructionsBelongingToOtherPrograms()
    {
        var keys = Keys();
        keys.Add("SomeOtherProgram1111111111111111111111111111");

        BuyTrace.FindBondingCurveV2(keys, [Buy()]).ShouldBeNull();
    }

    [Fact]
    public void FindBondingCurveV2_IgnoresPumpInstructionsThatAreNotBuys()
    {
        var keys = Keys();
        keys.Add(PumpProgram);

        var sell = new TraceInstruction(18, [.. Enumerable.Range(0, 18)], [.. new byte[8], .. new byte[16]]);

        BuyTrace.FindBondingCurveV2(keys, [sell]).ShouldBeNull();
    }

    /// <summary>
    /// A buy carrying only the IDL's 16 accounts predates the trailing pair — it tells us nothing, and
    /// silently returning key 15 would send a wrong account into a real buy.
    /// </summary>
    [Fact]
    public void FindBondingCurveV2_ReturnsNullWhenTheBuyCarriesOnlyTheSixteenIdlAccounts()
    {
        var keys = Keys();
        keys.Add(PumpProgram);

        BuyTrace.FindBondingCurveV2(keys, [Buy(accountCount: 16)]).ShouldBeNull();
    }

    [Fact]
    public void FindBondingCurveV2_TakesTheFirstBuyWhenATransactionCarriesSeveral()
    {
        var keys = Keys();
        keys.Add(PumpProgram);
        keys.Add("laterCurveV2111111111111111111111111111111111");

        var second = new TraceInstruction(18, [.. Enumerable.Range(0, 16), 19, 17], BuyData());

        BuyTrace.FindBondingCurveV2(keys, [Buy(), second])!.Key.ShouldBe(BondingCurveV2);
    }
}
