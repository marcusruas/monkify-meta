using Solnet.Wallet;
using SpaceStroker.Nightfall.Domain;

namespace SpaceStroker.Nightfall.Infrastructure.Solana;

/// <summary>
/// Associated-token-account derivation that takes the token program EXPLICITLY.
///
/// ⚠️ The token program is one of the ATA's seeds — [owner, tokenProgram, mint] — so it is part of the
/// address, not a detail of how the account is later used. Solnet's
/// <c>AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount</c> hardcodes SPL Token, and pump.fun
/// issues mints under BOTH SPL Token and Token-2022. Deriving a Token-2022 holding with SPL Token
/// yields a well-formed address that does not exist, and a non-existent account reads as a ZERO
/// balance (by design — see SolanaReader): a wallet holding millions showed as empty and unsellable.
///
/// SpaceStroker.TokenSniper's PumpFunProgram carries the same derivation for the buy side, learned the
/// same way. The two tools are deliberately standalone, so this is a mirror rather than a shared
/// library — keep them in step.
/// </summary>
public static class AssociatedToken
{
    private static readonly PublicKey AssociatedTokenProgram = new(SwapInstructionsGuard.AssociatedTokenAccountProgramId);

    public static string Derive(string owner, string mint, string tokenProgram)
    {
        var seeds = new[]
        {
            new PublicKey(owner).KeyBytes,
            new PublicKey(tokenProgram).KeyBytes,
            new PublicKey(mint).KeyBytes,
        };

        if (!PublicKey.TryFindProgramAddress(seeds, AssociatedTokenProgram, out var address, out _))
            throw new NightfallException($"Could not derive the token account for {owner}.");

        return address.Key;
    }

    /// <summary>
    /// Guards the one thing that would make every derived address silently wrong: a mint that is not
    /// owned by a token program at all. Returns the program id so a caller can assign it in one step.
    /// </summary>
    public static string EnsureTokenProgram(string ownerProgram, string mint)
    {
        if (ownerProgram != SwapInstructionsGuard.TokenProgramId && ownerProgram != SwapInstructionsGuard.Token2022ProgramId)
            throw new NightfallException(
                $"The mint {mint} is owned by {ownerProgram}, which is not an SPL token program. "
                + "Check nightfall.token.mint — this must be the token's mint address (the CA), not a wallet or a pool account.");

        return ownerProgram;
    }

    /// <summary>For the banner: which of the two programs the configured mint actually uses.</summary>
    public static string Describe(string tokenProgram)
        => tokenProgram == SwapInstructionsGuard.Token2022ProgramId ? "Token-2022" : "SPL Token";
}
