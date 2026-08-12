using Shouldly;
using Solnet.Wallet;
using SpaceStroker.TokenSniper.Domain;
using SpaceStroker.TokenSniper.Infrastructure.Solana;
using Xunit;

namespace SpaceStroker.TokenSniper.Tests.Infrastructure;

/// <summary>
/// Decoded against a REAL bonding-curve account dump (mainnet,
/// EheUZtKj6sgmzgyyYi2vEexHnWJJgz6oE8jmXw1f7Kfr, captured 2026-08-11). Reserve fields in the wrong
/// order still decode to plausible numbers and would silently misprice every buy — so the layout is
/// asserted against bytes, not described in a comment.
/// </summary>
public class AccountLayoutsTests
{
    private const string LiveCurveBase64 =
        "F7f4N2DYrGBm10LvgxYCAOHxrsAMAAAAZj8wo/IXAQDhRYvEBQAAAACAxqR+jQMAAEySOogl9Y8fujiof41mzOBpL/LJ" +
        "xFeZJF/yMR+mzo3SAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==";

    [Fact]
    public void DecodeBondingCurve_ReadsTheLiveAccountExactly()
    {
        var curve = AccountLayouts.DecodeBondingCurve(Convert.FromBase64String(LiveCurveBase64));

        curve.VirtualTokenReserves.ShouldBe(587_705_864_083_302ul);
        curve.VirtualQuoteReserves.ShouldBe(54_772_298_209ul);
        curve.RealTokenReserves.ShouldBe(307_805_864_083_302ul);
        curve.RealQuoteReserves.ShouldBe(24_772_298_209ul);
        curve.TokenTotalSupply.ShouldBe(1_000_000_000_000_000ul);
        curve.Complete.ShouldBeFalse();
        curve.Creator.ShouldBe("69uK17vg5VqGQ7xVRRRhGgGeddhvXiTKREmbnXRDaZLV");
        curve.IsMayhemMode.ShouldBeFalse();
        curve.IsCashbackCoin.ShouldBeFalse();

        // The default pubkey ⇒ this curve is quoted in SOL. Anything else and the tool refuses.
        curve.QuoteMint.ShouldBe(BondingCurve.SolQuoteMint);
        BondingCurve.IsSolQuoted(curve).ShouldBeTrue();
        BondingCurve.IsGraduated(curve).ShouldBeFalse();
    }

    [Fact]
    public void DecodeBondingCurve_RejectsAnAccountWithTheWrongDiscriminator()
    {
        var bytes = Convert.FromBase64String(LiveCurveBase64);
        bytes[0] ^= 0xFF;

        Should.Throw<SniperException>(() => AccountLayouts.DecodeBondingCurve(bytes))
            .Message.ShouldContain("discriminator");
    }

    [Fact]
    public void DecodeBondingCurve_RejectsATruncatedAccount()
    {
        var bytes = Convert.FromBase64String(LiveCurveBase64)[..40];

        Should.Throw<SniperException>(() => AccountLayouts.DecodeBondingCurve(bytes));
    }

    [Fact]
    public void DecodeGlobal_ReadsTheFeeRatesAndRecipient()
    {
        var global = AccountLayouts.DecodeGlobal(SyntheticGlobal());

        global.FeeRecipient.ShouldBe("11111111111111111111111111111111");
        global.Fees.FeeBasisPoints.ShouldBe(95ul);              // mainnet value on 2026-08-11
        global.Fees.CreatorFeeBasisPoints.ShouldBe(5ul);
        global.Fees.TotalBasisPoints.ShouldBe(100ul);           // 1% all-in
    }

    /// <summary>
    /// The deployed program requires one of these as a trailing account on every buy, so reading them
    /// is not optional bookkeeping — a buy without one is rejected with BuybackFeeRecipientMissing.
    /// They sit at offset 741, far past the fields this tool used to read.
    /// </summary>
    [Fact]
    public void DecodeGlobal_ReadsTheEightBuybackFeeRecipients()
    {
        var global = AccountLayouts.DecodeGlobal(SyntheticGlobal());

        global.BuybackFeeRecipients.Count.ShouldBe(8);
        global.BuybackFeeRecipients[0].ShouldBe(new PublicKey(Filled(1)).Key);
        global.BuybackFeeRecipients[7].ShouldBe(new PublicKey(Filled(8)).Key);
    }

    /// <summary>
    /// 1045 bytes is the size of the REAL global account on mainnet (read 2026-08-12). If the synthetic
    /// fixture drifts from it, every offset past the drift is wrong and the recipients decode to junk.
    /// </summary>
    [Fact]
    public void SyntheticGlobal_IsTheSameSizeAsTheLiveAccount()
    {
        SyntheticGlobal().Length.ShouldBe(1045);
    }

    private static byte[] Filled(byte value) => Enumerable.Repeat(value, 32).ToArray();

    /// <summary>
    /// A byte-accurate Global in the IDL's field order, carrying the live fee rates. Built here rather
    /// than dumped because the real account is a kilobyte of mostly recipients we do not read.
    /// Field order and sizes come from the on-chain IDL's Global struct (read 2026-08-12).
    /// </summary>
    private static byte[] SyntheticGlobal()
    {
        var bytes = new List<byte>();
        bytes.AddRange(AccountLayouts.GlobalDiscriminator);
        bytes.Add(1);                                    // initialized
        bytes.AddRange(new byte[32]);                    // authority
        bytes.AddRange(new byte[32]);                    // fee_recipient (default pubkey)
        bytes.AddRange(BitConverter.GetBytes(1_073_000_000_000_000ul));
        bytes.AddRange(BitConverter.GetBytes(30_000_000_000ul));
        bytes.AddRange(BitConverter.GetBytes(793_100_000_000_000ul));
        bytes.AddRange(BitConverter.GetBytes(1_000_000_000_000_000ul));
        bytes.AddRange(BitConverter.GetBytes(95ul));     // fee_basis_points
        bytes.AddRange(new byte[32]);                    // withdraw_authority
        bytes.Add(1);                                    // enable_migrate
        bytes.AddRange(BitConverter.GetBytes(0ul));      // pool_migration_fee
        bytes.AddRange(BitConverter.GetBytes(5ul));      // creator_fee_basis_points
        for (var i = 0; i < 7; i++) bytes.AddRange(new byte[32]);   // fee_recipients
        bytes.AddRange(new byte[32]);                    // set_creator_authority
        bytes.AddRange(new byte[32]);                    // admin_set_creator_authority
        bytes.Add(0);                                    // create_v2_enabled
        bytes.AddRange(new byte[32]);                    // whitelist_pda
        bytes.AddRange(new byte[32]);                    // reserved_fee_recipient
        bytes.Add(0);                                    // mayhem_mode_enabled
        for (var i = 0; i < 7; i++) bytes.AddRange(new byte[32]);   // reserved_fee_recipients
        bytes.Add(1);                                    // is_cashback_enabled
        for (byte i = 1; i <= 8; i++) bytes.AddRange(Filled(i));    // buyback_fee_recipients
        bytes.AddRange(BitConverter.GetBytes(0ul));      // buyback_basis_points
        bytes.AddRange(BitConverter.GetBytes(30_000_000_000ul));    // initial_virtual_quote_reserves
        bytes.AddRange(new byte[32]);                    // whitelisted_quote_mints

        return bytes.ToArray();
    }
}
