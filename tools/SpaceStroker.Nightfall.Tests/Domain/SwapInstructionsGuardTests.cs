using Shouldly;
using SpaceStroker.Nightfall.Domain;
using Xunit;

namespace SpaceStroker.Nightfall.Tests.Domain;

public class SwapInstructionsGuardTests
{
    private const string Wallet = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";
    private const string TokenMint = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v";
    private const string Sol = TokenAmount.WrappedSolMint;

    private static GuardedInstruction Instruction(string programId, params string[] accounts)
        => new(programId, accounts.Length == 0 ? [Wallet] : accounts, "");

    private static List<GuardedInstruction> CleanSet() =>
    [
        Instruction(SwapInstructionsGuard.ComputeBudgetProgramId),
        Instruction(SwapInstructionsGuard.AssociatedTokenAccountProgramId),
        Instruction(SwapInstructionsGuard.JupiterAggregatorV6ProgramId),
        Instruction(SwapInstructionsGuard.TokenProgramId),
    ];

    private static void Assert(IEnumerable<GuardedInstruction> instructions, ulong planned = 1_000UL,
        string quotedInput = TokenMint, string quotedOutput = Sol, ulong quotedIn = 1_000UL)
        => SwapInstructionsGuard.Assert(instructions, Wallet, TokenMint, quotedInput, quotedOutput, planned, quotedIn);

    [Fact]
    public void A_clean_jupiter_instruction_set_passes()
    {
        Should.NotThrow(() => Assert(CleanSet()));
    }

    [Fact]
    public void An_unknown_program_is_refused_and_named()
    {
        // This is the whole point of taking swap-instructions instead of a prebuilt transaction:
        // we get to see, and refuse, anything that is not on the allowlist before signing.
        var rogue = CleanSet();
        rogue.Add(Instruction("Rogue111111111111111111111111111111111111111"));

        var ex = Should.Throw<NightfallException>(() => Assert(rogue));
        ex.Message.ShouldContain("Rogue111111111111111111111111111111111111111");
    }

    [Fact]
    public void A_quote_for_a_different_input_mint_is_refused()
    {
        // Selling the wrong asset out of the wallet is unrecoverable.
        var ex = Should.Throw<NightfallException>(() =>
            Assert(CleanSet(), quotedInput: "So11111111111111111111111111111111111111112"));

        ex.Message.ShouldContain("input mint");
    }

    [Fact]
    public void A_quote_that_does_not_output_sol_is_refused()
    {
        var ex = Should.Throw<NightfallException>(() => Assert(CleanSet(), quotedOutput: TokenMint));

        ex.Message.ShouldContain("output mint");
    }

    [Fact]
    public void A_quote_for_a_different_amount_than_planned_is_refused()
    {
        // Guards against acting on a stale or mismatched quote — the amount we approved in the
        // confirmation dialog must be the amount the instructions actually move.
        var ex = Should.Throw<NightfallException>(() => Assert(CleanSet(), planned: 1_000UL, quotedIn: 9_999UL));

        ex.Message.ShouldContain("9999");
    }

    [Fact]
    public void An_instruction_set_that_never_touches_the_aggregator_is_refused()
    {
        // A "swap" carrying no swap is not a swap.
        var withoutJupiter = CleanSet().Where(i => i.ProgramId != SwapInstructionsGuard.JupiterAggregatorV6ProgramId);

        var ex = Should.Throw<NightfallException>(() => Assert(withoutJupiter));
        ex.Message.ShouldContain("aggregator");
    }

    [Fact]
    public void An_instruction_that_does_not_involve_our_wallet_is_refused()
    {
        var foreign = CleanSet();
        foreign.Add(Instruction(SwapInstructionsGuard.TokenProgramId, "9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM"));

        var ex = Should.Throw<NightfallException>(() => Assert(foreign));
        ex.Message.ShouldContain("does not involve");
    }

    [Fact]
    public void An_empty_instruction_set_is_refused()
    {
        Should.Throw<NightfallException>(() => Assert([]));
    }
}
