using Solnet.Wallet;
using SpaceStroker.TokenSniper.Domain;

namespace SpaceStroker.TokenSniper.Infrastructure.Solana;

/// <summary>
/// Borsh decoders for the two pump.fun accounts this tool reads.
///
/// ⚠️ Layouts and discriminators come from the program's on-chain Anchor IDL, read 2026-08-11, and are
/// asserted in tests against a real account dump. Decoding reserves in the wrong order produces a
/// plausible but WRONG price, which is why the discriminator is checked first rather than assumed.
/// </summary>
public static class AccountLayouts
{
    public static readonly byte[] BondingCurveDiscriminator = [23, 183, 248, 55, 96, 216, 172, 96];
    public static readonly byte[] GlobalDiscriminator = [167, 232, 232, 177, 200, 108, 114, 127];

    /// <summary>
    /// BondingCurve: virtual_token_reserves u64 · virtual_quote_reserves u64 · real_token_reserves u64 ·
    /// real_quote_reserves u64 · token_total_supply u64 · complete bool · creator pubkey ·
    /// is_mayhem_mode bool · is_cashback_coin bool · quote_mint pubkey.
    /// </summary>
    public static CurveState DecodeBondingCurve(ReadOnlySpan<byte> data)
    {
        if (data.Length < 115)
            throw new SniperException($"The bonding-curve account is {data.Length} bytes — too short to decode.");

        if (!data[..8].SequenceEqual(BondingCurveDiscriminator))
            throw new SniperException(
                "The account at the bonding-curve address is not a pump.fun BondingCurve (discriminator mismatch). " +
                "Either the mint is wrong or the program was redeployed with a new layout — re-derive it from a live buy.");

        var offset = 8;

        var virtualTokens = ReadU64(data, ref offset);
        var virtualQuote = ReadU64(data, ref offset);
        var realTokens = ReadU64(data, ref offset);
        var realQuote = ReadU64(data, ref offset);
        var totalSupply = ReadU64(data, ref offset);
        var complete = data[offset++] != 0;
        var creator = new PublicKey(data.Slice(offset, 32).ToArray()); offset += 32;
        var mayhem = data[offset++] != 0;
        var cashback = data[offset++] != 0;
        var quoteMint = new PublicKey(data.Slice(offset, 32).ToArray());

        return new CurveState(
            virtualTokens, virtualQuote, realTokens, realQuote, totalSupply,
            complete, creator.Key, mayhem, cashback, quoteMint.Key);
    }

    /// <summary>
    /// Global — we need the fee recipient and the two fee rates. Layout:
    /// initialized bool · authority pubkey · fee_recipient pubkey · initial_virtual_token_reserves u64 ·
    /// initial_virtual_sol_reserves u64 · initial_real_token_reserves u64 · token_total_supply u64 ·
    /// fee_basis_points u64 · withdraw_authority pubkey · enable_migrate bool · pool_migration_fee u64 ·
    /// creator_fee_basis_points u64 · fee_recipients [pubkey; 7] · set_creator_authority pubkey.
    /// Read at runtime — the rates are program config and have changed before (95 + 5 bps today).
    /// </summary>
    public static GlobalConfig DecodeGlobal(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 + 1 + 32 + 32 + 40 + 32 + 1 + 8 + 8)
            throw new SniperException($"The global account is {data.Length} bytes — too short to decode.");

        if (!data[..8].SequenceEqual(GlobalDiscriminator))
            throw new SniperException("The pump.fun global account has an unexpected discriminator — the program layout changed.");

        var offset = 8;
        offset += 1;                                                    // initialized
        offset += 32;                                                   // authority
        var feeRecipient = new PublicKey(data.Slice(offset, 32).ToArray()); offset += 32;
        offset += 8 * 4;                                                // the four initial-curve constants
        var feeBasisPoints = ReadU64(data, ref offset);
        offset += 32;                                                   // withdraw_authority
        offset += 1;                                                    // enable_migrate
        offset += 8;                                                    // pool_migration_fee
        var creatorFeeBasisPoints = ReadU64(data, ref offset);

        var recipients = new List<string>(7);
        for (var i = 0; i < 7 && offset + 32 <= data.Length; i++)
        {
            recipients.Add(new PublicKey(data.Slice(offset, 32).ToArray()).Key);
            offset += 32;
        }

        return new GlobalConfig(
            feeRecipient.Key, new CurveFees(feeBasisPoints, creatorFeeBasisPoints), recipients,
            ReadBuybackFeeRecipients(data));
    }

    /// <summary>Absolute offset of <c>buyback_fee_recipients</c> in Global, per the on-chain IDL.</summary>
    public const int BuybackFeeRecipientsOffset = 741;

    public const int BuybackFeeRecipientCount = 8;

    /// <summary>
    /// The 8 accounts the deployed program will accept as a buy's trailing fee recipient. Read at an
    /// ABSOLUTE offset rather than by walking the fields: everything between creator_fee_basis_points
    /// and here (whitelist_pda, reserved_fee_recipients, the mayhem and cashback flags…) is config this
    /// tool never reads, and walking it would make every one of those fields a chance to drift.
    ///
    /// An account too short to contain them decodes to an empty list — a pre-buyback deployment. The
    /// caller turns that into an explicit preflight error rather than submitting a buy that cannot land.
    /// </summary>
    private static IReadOnlyList<string> ReadBuybackFeeRecipients(ReadOnlySpan<byte> data)
    {
        var end = BuybackFeeRecipientsOffset + BuybackFeeRecipientCount * 32;
        if (data.Length < end) return [];

        var recipients = new List<string>(BuybackFeeRecipientCount);
        for (var i = 0; i < BuybackFeeRecipientCount; i++)
        {
            var offset = BuybackFeeRecipientsOffset + i * 32;
            recipients.Add(new PublicKey(data.Slice(offset, 32).ToArray()).Key);
        }

        return recipients;
    }

    private static ulong ReadU64(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BitConverter.ToUInt64(data.Slice(offset, 8));
        offset += 8;
        return value;
    }
}

public sealed record GlobalConfig(
    string FeeRecipient,
    CurveFees Fees,
    IReadOnlyList<string> FeeRecipients,
    IReadOnlyList<string> BuybackFeeRecipients);
