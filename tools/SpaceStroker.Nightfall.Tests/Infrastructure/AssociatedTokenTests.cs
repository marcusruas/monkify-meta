using Shouldly;
using Solnet.Programs;
using Solnet.Wallet;
using SpaceStroker.Nightfall.Domain;
using SpaceStroker.Nightfall.Infrastructure.Solana;
using Xunit;

namespace SpaceStroker.Nightfall.Tests.Infrastructure;

/// <summary>
/// ⚠️ The bug these pin down: an associated token account's address is derived from
/// [owner, TOKEN PROGRAM, mint], so the token program is part of the ADDRESS. Solnet's
/// <c>AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount</c> hardcodes SPL Token — and pump.fun
/// mints are issued under BOTH SPL Token and Token-2022. Deriving a Token-2022 holding with the SPL
/// Token program yields a valid-looking address that simply does not exist, which
/// <c>GetBalancesAsync</c> then reads as a ZERO balance: a wallet holding millions of tokens showed
/// up empty and unsellable. The sniper hit this first (SpaceStroker.TokenSniper's PumpFunProgram) —
/// this is the same lesson, enforced here.
///
/// The expected addresses below were computed INDEPENDENTLY with @solana/web3.js
/// (PublicKey.findProgramAddressSync), not with Solnet, so these are a cross-implementation check
/// rather than a restatement of our own code. The owner/mint are synthetic all-0x01 / all-0x02 keys:
/// a real pool address must never be committed.
/// </summary>
public class AssociatedTokenTests
{
    private const string Owner = "4vJ9JU1bJJE96FWSJKvHsmmFADCg4gpZQff4P3bkLKi";
    private const string Mint = "8qbHbw2BbbTHBW1sbeqakYXVKRQM8Ne7pLK7m6CVfeR";

    private const string ExpectedSplAta = "CsYkfSfTUTWwnoeRkGchtai5kkYz2SC33kKJwA99wVr3";
    private const string ExpectedToken2022Ata = "DyaUQ3JTcmWApDibKtBvxLBhUPjvA4KEM99t45qz3bfh";

    [Fact]
    public void Spl_token_derivation_matches_the_independently_computed_address()
        => AssociatedToken.Derive(Owner, Mint, SwapInstructionsGuard.TokenProgramId).ShouldBe(ExpectedSplAta);

    [Fact]
    public void Token_2022_derivation_matches_the_independently_computed_address()
        => AssociatedToken.Derive(Owner, Mint, SwapInstructionsGuard.Token2022ProgramId).ShouldBe(ExpectedToken2022Ata);

    [Fact]
    public void The_token_program_changes_the_address()
    {
        // If this ever passes trivially, the token program stopped being a seed and every Token-2022
        // balance silently reads as zero again.
        AssociatedToken.Derive(Owner, Mint, SwapInstructionsGuard.TokenProgramId)
            .ShouldNotBe(AssociatedToken.Derive(Owner, Mint, SwapInstructionsGuard.Token2022ProgramId));
    }

    [Fact]
    public void The_spl_token_case_still_agrees_with_Solnets_own_helper()
    {
        // Solnet is only correct for SPL Token — pinning that agreement proves the hand-rolled PDA is
        // right, and localises the divergence to the program seed rather than to our maths.
        var solnet = AssociatedTokenAccountProgram
            .DeriveAssociatedTokenAccount(new PublicKey(Owner), new PublicKey(Mint))!.Key;

        AssociatedToken.Derive(Owner, Mint, SwapInstructionsGuard.TokenProgramId).ShouldBe(solnet);
    }

    [Fact]
    public void A_reader_built_for_a_Token_2022_mint_derives_the_Token_2022_account()
    {
        // The wiring, not just the maths: SolanaReader must carry the mint's own program.
        var reader = new SolanaReader(null!, Mint, SwapInstructionsGuard.Token2022ProgramId);

        reader.DeriveAta(Owner).ShouldBe(ExpectedToken2022Ata);
    }

    [Fact]
    public void A_mint_owned_by_something_that_is_not_a_token_program_is_refused()
    {
        // A wrong CA (an ordinary account, or a pool address pasted by mistake) must fail loudly at
        // startup rather than derive nonsense addresses and render a dashboard full of zeroes.
        var notATokenProgram = Should.Throw<NightfallException>(
            () => AssociatedToken.EnsureTokenProgram(SwapInstructionsGuard.JupiterAggregatorV6ProgramId, Mint));

        notATokenProgram.Message.ShouldContain(Mint);
    }
}
