using Shouldly;
using Solnet.Wallet;
using SpaceStroker.TokenSniper.Domain;
using SpaceStroker.TokenSniper.Infrastructure.Solana;
using Xunit;

namespace SpaceStroker.TokenSniper.Tests.Infrastructure;

/// <summary>
/// The account list is the one part of this tool that cannot be reasoned out — it has to be read off
/// the chain. These tests pin it to a REAL mainnet buy so a pump.fun redeployment fails here rather
/// than at the operator's first live run.
///
/// Fixture: signature
/// 2kHXxyAqoiZfy3ucR8TaM7PSPms2HuBpHTagiAWBmCSCDiZ1F6zfrgZAMNLwTHJ85FuKnFxN1NtFGPYkNnYSLjLo
/// (mainnet, captured 2026-08-11) — amount 7,404,182,659,944 base units, maxSolCost 540,000,000
/// lamports, 24-byte instruction data (the IDL's third `track_volume` arg omitted).
/// </summary>
public class PumpFunProgramTests
{
    private const string Mint = "6RTQtuhu19q4LnCxbJLPEWjS1USoqAbSBLkM93tXpump";

    /// <summary>
    /// A REAL Token-2022 pump.fun mint (mainnet, read 2026-08-11). pump.fun mints freshly created
    /// tokens under EITHER token program, so both must be exercised — a tool that only handles SPL
    /// Token fails 100% of the time on the other half of launches.
    /// </summary>
    private const string Token2022Mint = "HxeW7sEiPwXph8LjotmSYKCetsJpbmNfZygS4V7dpump";

    private const string SplTokenProgram = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";
    private const string Token2022Program = "TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb";

    private const string User = "7Q6RcQKsmpaRsaK17nuVyxDEWMRcPJUhboHvsqNRgTsR";

    /// <summary>The two trailing accounts, read off a live buy (mainnet, 2026-08-12).</summary>
    private const string BondingCurveV2 = "4Twez7HGGNc9ef4HpddGWNE3GWk9w8eygtvkpmU1gVCU";
    private const string BuybackRecipient = "GXPFM2caqTtQYC2cJ5yJRi9VDkpsYZXzYdwYpGnLmtDL";
    private const string Creator = "69uK17vg5VqGQ7xVRRRhGgGeddhvXiTKREmbnXRDaZLV";
    private const string FeeRecipient = "CebN5WGQ4jvEPvsVU4EoHEpgzq1VV7AbicfhtW4xC9iM";

    private static readonly string[] LiveAccounts =
    [
        "4wTV1YmiEkRvAtNtsSGPtUrqRYQMe5SKy2uB4Jjaxnjf",   //  0 global
        FeeRecipient,                                      //  1 fee_recipient
        Mint,                                              //  2 mint
        "EheUZtKj6sgmzgyyYi2vEexHnWJJgz6oE8jmXw1f7Kfr",   //  3 bonding_curve
        "6AUhTZGpSX9M7jpEUXp8XaPSSN2NduNLDxisd7Kxym9e",   //  4 associated_bonding_curve
        "9qAFYjgBZFwqnKNoGLdLjXZfDR5YY5uWz2pSaXiiVe6q",   //  5 associated_user
        User,                                              //  6 user
        "11111111111111111111111111111111",                //  7 system_program
        "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA",    //  8 token_program
        "AGPKDPRYzGFgrHeRkAzEUTBzTgCZB8H5LAhsWa9CiMmf",   //  9 creator_vault
        "Ce6TQqeHC9p8KetsN6JsjHK7UTZk7nasjjnr7XxXp9F1",   // 10 event_authority
        "6EF8rrecthR5Dkzon8Nwu78hRvfCKubJ14M5uBEwF6P",    // 11 program
        "Hq2wp8uJ9jCPsYgNHex8RtqdvMPfVGoYwjvF1ATiwn2Y",   // 12 global_volume_accumulator
        "FE2Da12L9Kv2jSdWcuXinJyfDBqjtzRBcwdicRZumxWS",   // 13 user_volume_accumulator
        "8Wf5TiAheLUqBrKXeYg2JtAFFMWtKdG2BSFgqUcPVwTt",   // 14 fee_config
        "pfeeUxB6jkeY1Hxd7CsFCAjcbHA9rWtchMGdZ6VojVZ",    // 15 fee_program
    ];

    [Fact]
    public void BuyDiscriminator_IsTheSha256OfTheAnchorGlobalName()
    {
        // Computed, never pasted — but it must equal what the chain actually accepts.
        Convert.ToHexString(PumpFunProgram.BuyDiscriminator).ToLowerInvariant().ShouldBe("66063d1201daebea");
    }

    [Fact]
    public void EveryDerivedAccount_MatchesTheLiveTransaction()
    {
        PumpFunProgram.DeriveGlobal().Key.ShouldBe(LiveAccounts[0]);
        PumpFunProgram.DeriveBondingCurve(new PublicKey(Mint)).Key.ShouldBe(LiveAccounts[3]);
        PumpFunProgram.DeriveCreatorVault(new PublicKey(Creator)).Key.ShouldBe(LiveAccounts[9]);
        PumpFunProgram.DeriveEventAuthority().Key.ShouldBe(LiveAccounts[10]);
        PumpFunProgram.DeriveGlobalVolumeAccumulator().Key.ShouldBe(LiveAccounts[12]);
        PumpFunProgram.DeriveUserVolumeAccumulator(new PublicKey(User)).Key.ShouldBe(LiveAccounts[13]);
        PumpFunProgram.DeriveFeeConfig().Key.ShouldBe(LiveAccounts[14]);
    }

    [Fact]
    public void AssociatedTokenAccounts_AreDerivedWithTheMintsOwnTokenProgram()
    {
        var mint = new PublicKey(Mint);

        PumpFunProgram.DeriveAssociatedTokenAccount(new PublicKey(LiveAccounts[3]), mint, PumpFunProgram.TokenProgramId)
            .Key.ShouldBe(LiveAccounts[4]);

        PumpFunProgram.DeriveAssociatedTokenAccount(new PublicKey(User), mint, PumpFunProgram.TokenProgramId)
            .Key.ShouldBe(LiveAccounts[5]);

        // Token-2022 mints exist in live pump.fun traffic; the same owner+mint must derive elsewhere.
        PumpFunProgram.DeriveAssociatedTokenAccount(new PublicKey(User), mint, PumpFunProgram.Token2022ProgramId)
            .Key.ShouldNotBe(LiveAccounts[5]);
    }

    [Fact]
    public void BuildBuyInstruction_MatchesALiveMainnetBuy()
    {
        var instruction = PumpFunProgram.BuildBuyInstruction(BuyAccounts(), 7_404_182_659_944, 540_000_000);

        instruction.ProgramId.ShouldBe(PumpFunProgram.ProgramId.KeyBytes);
        instruction.Keys.Select(k => k.PublicKey).ShouldBe(LiveAccounts);

        // Flags matter as much as order: the wrong writable bit is a rejected transaction.
        instruction.Keys[6].IsSigner.ShouldBeTrue();
        instruction.Keys[6].IsWritable.ShouldBeTrue();
        instruction.Keys.Where((_, i) => i != 6).ShouldAllBe(k => !k.IsSigner);
        new[] { 1, 3, 4, 5, 6, 9, 13 }.ShouldAllBe(i => instruction.Keys[i].IsWritable);

        Convert.ToHexString(instruction.Data).ToLowerInvariant()
            .ShouldBe("66063d1201daebea" + "6863adebbb060000" + "00bf2f2000000000");
    }

    [Theory]
    [InlineData(null, 24)]   // omitted — the most common encoding in live traffic, and our default
    [InlineData(false, 25)]  // a single 0x00
    [InlineData(true, 26)]   // 0x01 0x01
    public void TrackVolume_EncodesTheThreeFormsSeenOnChain(bool? trackVolume, int expectedLength)
    {
        var instruction = PumpFunProgram.BuildBuyInstruction(BuyAccounts(), 1, 2, trackVolume);

        instruction.Data.Length.ShouldBe(expectedLength);
    }

    [Fact]
    public void ExtraAccounts_AreAppendedAfterTheSixteenTheIdlDeclares()
    {
        var extra = new PublicKey("So11111111111111111111111111111111111111112");

        var instruction = PumpFunProgram.BuildBuyInstruction(BuyAccounts(), 1, 2, null, [extra]);

        instruction.Keys.Count.ShouldBe(17);
        instruction.Keys[16].PublicKey.ShouldBe(extra.Key);
    }

    /// <summary>
    /// The deployed program reads two accounts past the IDL's 16. Without them it aborts with
    /// BuybackFeeRecipientMissing (6062); with the recipient marked read-only it aborts with
    /// PrivilegeEscalation — the program moves SOL into it. Both were observed in live simulation.
    /// </summary>
    [Fact]
    public void BuildBuyInstruction_AppendsBondingCurveV2ThenAWritableBuybackFeeRecipient()
    {
        var curveV2 = new PublicKey(BondingCurveV2);
        var recipient = new PublicKey(BuybackRecipient);

        var instruction = PumpFunProgram.BuildBuyInstruction(
            BuyAccounts(), 1, 2, bondingCurveV2: curveV2, buybackFeeRecipient: recipient);

        instruction.Keys.Count.ShouldBe(18);

        instruction.Keys[16].PublicKey.ShouldBe(curveV2.Key);
        instruction.Keys[16].IsWritable.ShouldBeFalse();
        instruction.Keys[16].IsSigner.ShouldBeFalse();

        instruction.Keys[17].PublicKey.ShouldBe(recipient.Key);
        instruction.Keys[17].IsWritable.ShouldBeTrue();
        instruction.Keys[17].IsSigner.ShouldBeFalse();
    }

    [Fact]
    public void ConfiguredExtraAccounts_ComeAfterTheTwoTrailingAccounts()
    {
        var extra = new PublicKey("So11111111111111111111111111111111111111112");

        var instruction = PumpFunProgram.BuildBuyInstruction(
            BuyAccounts(), 1, 2, null, [extra],
            new PublicKey(BondingCurveV2), new PublicKey(BuybackRecipient));

        instruction.Keys.Count.ShouldBe(19);
        instruction.Keys[16].PublicKey.ShouldBe(BondingCurveV2);
        instruction.Keys[17].PublicKey.ShouldBe(BuybackRecipient);
        instruction.Keys[18].PublicKey.ShouldBe(extra.Key);
    }

    /// <summary>
    /// Positional accounts: supplying one without the other silently shifts the recipient into the
    /// curve's slot, which the program would read as a wrong-but-present account.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void BuildBuyInstruction_RejectsOnlyOneOfTheTwoTrailingAccounts(bool withCurve, bool withRecipient)
    {
        Should.Throw<SniperException>(() => PumpFunProgram.BuildBuyInstruction(
                BuyAccounts(), 1, 2, null, null,
                withCurve ? new PublicKey(BondingCurveV2) : null,
                withRecipient ? new PublicKey(BuybackRecipient) : null))
            .Message.ShouldContain("together");
    }

    /// <summary>
    /// The account the create instruction opens MUST be the very account the buy then names as
    /// <c>associated_user</c>. Deriving those two with different token programs opens one account and
    /// spends into another — the buy fails, and it fails only for mints under the other token program.
    /// </summary>
    [Theory]
    [InlineData(Mint, SplTokenProgram)]
    [InlineData(Token2022Mint, Token2022Program)]
    public void CreateAssociatedTokenAccount_TargetsTheAccountTheBuyWillReference(string mintAddress, string tokenProgram)
    {
        var mint = new PublicKey(mintAddress);
        var program = new PublicKey(tokenProgram);
        var owner = new PublicKey(User);

        var instruction = PumpFunProgram.BuildCreateAssociatedTokenAccount(owner, owner, mint, program);

        instruction.Keys[1].PublicKey.ShouldBe(
            PumpFunProgram.DeriveAssociatedTokenAccount(owner, mint, program).Key);
    }

    [Theory]
    [InlineData(Mint, SplTokenProgram)]
    [InlineData(Token2022Mint, Token2022Program)]
    public void CreateAssociatedTokenAccount_CarriesTheMintsOwnTokenProgram(string mintAddress, string tokenProgram)
    {
        var mint = new PublicKey(mintAddress);
        var program = new PublicKey(tokenProgram);
        var owner = new PublicKey(User);

        var instruction = PumpFunProgram.BuildCreateAssociatedTokenAccount(owner, owner, mint, program);

        instruction.ProgramId.ShouldBe(PumpFunProgram.AssociatedTokenProgramId.KeyBytes);
        instruction.Keys.Select(k => k.PublicKey).ShouldBe(
        [
            User,                                              // 0 payer
            PumpFunProgram.DeriveAssociatedTokenAccount(owner, mint, program).Key,  // 1 the ATA
            User,                                              // 2 owner
            mintAddress,                                       // 3 mint
            PumpFunProgram.SystemProgramId.Key,                // 4 system_program
            tokenProgram,                                      // 5 token_program — the mint's own
        ]);

        instruction.Keys[0].IsSigner.ShouldBeTrue();
        instruction.Keys[0].IsWritable.ShouldBeTrue();
        instruction.Keys[1].IsWritable.ShouldBeTrue();
        instruction.Keys.Where((_, i) => i != 0).ShouldAllBe(k => !k.IsSigner);
        instruction.Keys.Where((_, i) => i != 0 && i != 1).ShouldAllBe(k => !k.IsWritable);
    }

    /// <summary>
    /// Discriminator 1 is CreateIdempotent. Plain Create (empty data) aborts the WHOLE transaction if
    /// the account already exists — and this tool resumes runs, so an ATA opened by an earlier attempt
    /// is a normal state, not an error.
    /// </summary>
    [Fact]
    public void CreateAssociatedTokenAccount_IsIdempotent()
    {
        var instruction = PumpFunProgram.BuildCreateAssociatedTokenAccount(
            new PublicKey(User), new PublicKey(User), new PublicKey(Token2022Mint), PumpFunProgram.Token2022ProgramId);

        instruction.Data.ShouldBe([(byte)1]);
    }

    /// <summary>
    /// Why this instruction is hand-built. Solnet's helper hardcodes SPL Token in BOTH the derived
    /// address and the token_program account, so on a Token-2022 mint it fails with "incorrect program
    /// id for instruction" — and if it somehow passed, it would open the wrong account.
    /// </summary>
    [Fact]
    public void CreateAssociatedTokenAccount_DoesNotMatchSolnetsHelperOnAToken2022Mint()
    {
        var owner = new PublicKey(User);
        var mint = new PublicKey(Token2022Mint);

        var ours = PumpFunProgram.BuildCreateAssociatedTokenAccount(
            owner, owner, mint, PumpFunProgram.Token2022ProgramId);
        var solnet = Solnet.Programs.AssociatedTokenAccountProgram.CreateAssociatedTokenAccount(owner, owner, mint);

        solnet.Keys[5].PublicKey.ShouldBe(SplTokenProgram);
        ours.Keys[5].PublicKey.ShouldBe(Token2022Program);
        ours.Keys[1].PublicKey.ShouldNotBe(solnet.Keys[1].PublicKey);
    }

    [Fact]
    public void ComputeBudgetInstructions_UseTheDocumentedOpcodes()
    {
        // Solnet.Programs 6.1.0 has no ComputeBudgetProgram, so these are hand-built:
        // [2, u32 units] and [3, u64 microLamports].
        var limit = PumpFunProgram.SetComputeUnitLimit(250_000);
        limit.ProgramId.ShouldBe(PumpFunProgram.ComputeBudgetProgramId.KeyBytes);
        limit.Data[0].ShouldBe((byte)2);
        BitConverter.ToUInt32(limit.Data, 1).ShouldBe(250_000u);

        var price = PumpFunProgram.SetComputeUnitPrice(200_000);
        price.Data[0].ShouldBe((byte)3);
        BitConverter.ToUInt64(price.Data, 1).ShouldBe(200_000ul);
    }

    private static PumpFunProgram.BuyAccounts BuyAccounts() => new(
        FeeRecipient: new PublicKey(FeeRecipient),
        Mint: new PublicKey(Mint),
        BondingCurve: new PublicKey(LiveAccounts[3]),
        AssociatedBondingCurve: new PublicKey(LiveAccounts[4]),
        AssociatedUser: new PublicKey(LiveAccounts[5]),
        User: new PublicKey(User),
        TokenProgram: PumpFunProgram.TokenProgramId,
        CreatorVault: new PublicKey(LiveAccounts[9]));
}
