using System.Security.Cryptography;
using Solnet.Rpc.Models;
using Solnet.Wallet;
using SpaceStroker.TokenSniper.Domain;

namespace SpaceStroker.TokenSniper.Infrastructure.Solana;

/// <summary>
/// Everything about the pump.fun bonding-curve program's <c>buy</c> instruction.
///
/// ⚠️ EVERY value here was read off mainnet on 2026-08-11 — the program's own on-chain Anchor IDL
/// (<c>AYgC53tU5BbP2NAnv5nConJxAdpQZctvmZK88pu69xRs</c>) plus decoded live buys — and every PDA
/// derivation below is asserted against a real transaction in
/// <c>PumpFunProgramTests.BuildBuyInstruction_MatchesALiveMainnetBuy</c>. If pump.fun redeploys, that
/// test is what tells you, and re-deriving from a fresh transaction is the fix. Do not "correct" any
/// of this from documentation.
/// </summary>
public static class PumpFunProgram
{
    public static readonly PublicKey ProgramId = new("6EF8rrecthR5Dkzon8Nwu78hRvfCKubJ14M5uBEwF6P");
    public static readonly PublicKey FeeProgramId = new("pfeeUxB6jkeY1Hxd7CsFCAjcbHA9rWtchMGdZ6VojVZ");
    public static readonly PublicKey SystemProgramId = new("11111111111111111111111111111111");
    public static readonly PublicKey AssociatedTokenProgramId = new("ATokenGPvbdGVxr1b2hvZbsiqW5xWH25efTNsLJA8knL");
    public static readonly PublicKey TokenProgramId = new("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA");
    public static readonly PublicKey Token2022ProgramId = new("TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb");
    public static readonly PublicKey ComputeBudgetProgramId = new("ComputeBudget111111111111111111111111111111");

    /// <summary>Anchor's global namespace discriminator. COMPUTED, never a pasted hex constant.</summary>
    public static byte[] BuyDiscriminator { get; } = Discriminator("global:buy");

    public static byte[] Discriminator(string globalName) =>
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(globalName))[..8];

    public static PublicKey DeriveGlobal() => Pda([Bytes("global")], ProgramId);

    public static PublicKey DeriveBondingCurve(PublicKey mint) =>
        Pda([Bytes("bonding-curve"), mint.KeyBytes], ProgramId);

    public static PublicKey DeriveCreatorVault(PublicKey creator) =>
        Pda([Bytes("creator-vault"), creator.KeyBytes], ProgramId);

    public static PublicKey DeriveEventAuthority() => Pda([Bytes("__event_authority")], ProgramId);

    public static PublicKey DeriveGlobalVolumeAccumulator() =>
        Pda([Bytes("global_volume_accumulator")], ProgramId);

    public static PublicKey DeriveUserVolumeAccumulator(PublicKey user) =>
        Pda([Bytes("user_volume_accumulator"), user.KeyBytes], ProgramId);

    /// <summary>PDA["fee_config", &lt;bonding-curve program&gt;] — owned by the FEE program, not this one.</summary>
    public static PublicKey DeriveFeeConfig() =>
        Pda([Bytes("fee_config"), ProgramId.KeyBytes], FeeProgramId);

    /// <summary>
    /// ATA derivation that takes the token program explicitly.
    /// Solnet's <c>AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount</c> hardcodes SPL Token,
    /// but live pump.fun mints use BOTH SPL Token and Token-2022 — deriving with the wrong program
    /// yields a valid-looking address that the buy would then fail on.
    /// </summary>
    public static PublicKey DeriveAssociatedTokenAccount(PublicKey owner, PublicKey mint, PublicKey tokenProgram) =>
        Pda([owner.KeyBytes, tokenProgram.KeyBytes, mint.KeyBytes], AssociatedTokenProgramId);

    /// <summary>
    /// The ATA-create instruction, hand-built for the SAME reason
    /// <see cref="DeriveAssociatedTokenAccount"/> is: Solnet's
    /// <c>AssociatedTokenAccountProgram.CreateAssociatedTokenAccount</c> hardcodes SPL Token in BOTH the
    /// address it derives and the <c>token_program</c> account it passes. pump.fun mints new tokens
    /// under either token program, so on a Token-2022 mint that helper is rejected outright with
    /// "incorrect program id for instruction" — and had it passed, it would have opened an account the
    /// buy does not reference. The program is read off the mint at runtime and threaded through here.
    ///
    /// ⚠️ Data is <c>[1]</c> — CreateIdempotent, not Create. This tool resumes runs, so an ATA opened by
    /// an earlier attempt is a normal state; plain Create (empty data) would abort the whole
    /// transaction, buy included, the moment it met one.
    /// </summary>
    public static TransactionInstruction BuildCreateAssociatedTokenAccount(
        PublicKey payer, PublicKey owner, PublicKey mint, PublicKey tokenProgram) =>
        new()
        {
            ProgramId = AssociatedTokenProgramId.KeyBytes,
            Keys =
            [
                AccountMeta.Writable(payer, true),
                AccountMeta.Writable(DeriveAssociatedTokenAccount(owner, mint, tokenProgram), false),
                AccountMeta.ReadOnly(owner, false),
                AccountMeta.ReadOnly(mint, false),
                AccountMeta.ReadOnly(SystemProgramId, false),
                AccountMeta.ReadOnly(tokenProgram, false),
            ],
            Data = [1],
        };

    /// <summary>The 16 accounts the IDL declares for <c>buy</c>, in IDL order.</summary>
    public sealed record BuyAccounts(
        PublicKey FeeRecipient,
        PublicKey Mint,
        PublicKey BondingCurve,
        PublicKey AssociatedBondingCurve,
        PublicKey AssociatedUser,
        PublicKey User,
        PublicKey TokenProgram,
        PublicKey CreatorVault);

    /// <summary>
    /// Build the buy.
    ///
    /// ⚠️ <paramref name="trackVolume"/> is the IDL's third arg (<c>OptionBool</c>). Live traffic shows
    /// three encodings all landing on-chain: the arg omitted entirely (24-byte data, the most common),
    /// a single <c>00</c>, and <c>01 01</c>. We default to omitting it and expose the knob.
    ///
    /// ⚠️ <paramref name="extraAccounts"/> exists because live buys carry TWO trailing accounts the IDL
    /// does not declare. We build what the IDL declares; if a future deployment requires the extras,
    /// they can be configured without a code change. The failure mode is a reverted transaction — the
    /// SOL does not move.
    /// </summary>
    public static TransactionInstruction BuildBuyInstruction(
        BuyAccounts accounts,
        ulong amount,
        ulong maxSolCost,
        bool? trackVolume = null,
        IEnumerable<PublicKey>? extraAccounts = null,
        PublicKey? bondingCurveV2 = null,
        PublicKey? buybackFeeRecipient = null)
    {
        var keys = new List<AccountMeta>
        {
            AccountMeta.ReadOnly(DeriveGlobal(), false),
            AccountMeta.Writable(accounts.FeeRecipient, false),
            AccountMeta.ReadOnly(accounts.Mint, false),
            AccountMeta.Writable(accounts.BondingCurve, false),
            AccountMeta.Writable(accounts.AssociatedBondingCurve, false),
            AccountMeta.Writable(accounts.AssociatedUser, false),
            AccountMeta.Writable(accounts.User, true),
            AccountMeta.ReadOnly(SystemProgramId, false),
            AccountMeta.ReadOnly(accounts.TokenProgram, false),
            AccountMeta.Writable(accounts.CreatorVault, false),
            AccountMeta.ReadOnly(DeriveEventAuthority(), false),
            AccountMeta.ReadOnly(ProgramId, false),
            AccountMeta.ReadOnly(DeriveGlobalVolumeAccumulator(), false),
            AccountMeta.Writable(DeriveUserVolumeAccumulator(accounts.User), false),
            AccountMeta.ReadOnly(DeriveFeeConfig(), false),
            AccountMeta.ReadOnly(FeeProgramId, false),
        };

        // ⚠️ The two accounts the DEPLOYED program reads past the IDL's 16. Positional: supplying one
        // without the other would shift the recipient into the curve's slot, so both or neither.
        if (bondingCurveV2 is null != buybackFeeRecipient is null)
            throw new SniperException(
                "bondingCurveV2 and buybackFeeRecipient must be passed together — they are positional " +
                "trailing accounts and one without the other builds a buy the program misreads.");

        if (bondingCurveV2 is not null && buybackFeeRecipient is not null)
        {
            keys.Add(AccountMeta.ReadOnly(bondingCurveV2, false));
            keys.Add(AccountMeta.Writable(buybackFeeRecipient, false));   // the program moves SOL into it
        }

        foreach (var extra in extraAccounts ?? Enumerable.Empty<PublicKey>())
            keys.Add(AccountMeta.ReadOnly(extra, false));

        var data = new List<byte>(26);
        data.AddRange(BuyDiscriminator);
        data.AddRange(BitConverter.GetBytes(amount));
        data.AddRange(BitConverter.GetBytes(maxSolCost));
        if (trackVolume is true) data.AddRange([(byte)1, (byte)1]);
        else if (trackVolume is false) data.Add(0);

        return new TransactionInstruction
        {
            ProgramId = ProgramId.KeyBytes,
            Keys = keys,
            Data = data.ToArray(),
        };
    }

    /// <summary>
    /// Compute-budget instructions, hand-built.
    /// ⚠️ Solnet.Programs 6.1.0 has NO ComputeBudgetProgram (verified against the package), and
    /// monkify-backend never sets a priority fee, so there is no in-house helper to borrow. Without
    /// these a buy can simply never land in a busy launch window.
    /// Layout: [2, u32 units] = SetComputeUnitLimit · [3, u64 microLamports] = SetComputeUnitPrice.
    /// </summary>
    public static TransactionInstruction SetComputeUnitLimit(uint units) =>
        new()
        {
            ProgramId = ComputeBudgetProgramId.KeyBytes,
            Keys = new List<AccountMeta>(),
            Data = [(byte)2, .. BitConverter.GetBytes(units)],
        };

    public static TransactionInstruction SetComputeUnitPrice(ulong microLamports) =>
        new()
        {
            ProgramId = ComputeBudgetProgramId.KeyBytes,
            Keys = new List<AccountMeta>(),
            Data = [(byte)3, .. BitConverter.GetBytes(microLamports)],
        };

    private static byte[] Bytes(string seed) => System.Text.Encoding.UTF8.GetBytes(seed);

    private static PublicKey Pda(IEnumerable<byte[]> seeds, PublicKey programId)
    {
        if (!PublicKey.TryFindProgramAddress(seeds, programId, out var address, out _))
            throw new SniperException("Could not derive a program address — the seeds are wrong for this program.");

        return address;
    }
}
