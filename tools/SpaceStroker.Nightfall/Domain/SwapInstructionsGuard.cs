namespace SpaceStroker.Nightfall.Domain;

/// <summary>One instruction as Jupiter's /swap-instructions describes it, flattened for inspection.</summary>
public sealed record GuardedInstruction(string ProgramId, IReadOnlyList<string> Accounts, string DataBase64);

/// <summary>
/// The assertion that earns the design's choice of swap-INSTRUCTIONS over a prebuilt transaction.
///
/// Jupiter is a third party we ask to construct something we then sign with a key that controls real
/// holdings. Taking a ready transaction would mean signing bytes we never inspected; taking the
/// instructions lets us refuse anything that is not the swap we asked for — before a signature exists.
///
/// Pure by design, so every refusal below is a unit test rather than a live experiment.
/// </summary>
public static class SwapInstructionsGuard
{
    public const string JupiterAggregatorV6ProgramId = "JUP6LkbZbjS1jKKwapdHNy74zcZ3tLUZoi5QNyVTaV4";
    public const string TokenProgramId = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";
    public const string Token2022ProgramId = "TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb";
    public const string AssociatedTokenAccountProgramId = "ATokenGPvbdGVxr1b2hvZbsiqW5xWH25efTNsLJA8knL";
    public const string ComputeBudgetProgramId = "ComputeBudget111111111111111111111111111111";
    public const string SystemProgramId = "11111111111111111111111111111111";

    public static readonly IReadOnlySet<string> AllowedProgramIds = new HashSet<string>
    {
        JupiterAggregatorV6ProgramId,
        TokenProgramId,
        Token2022ProgramId,
        AssociatedTokenAccountProgramId,
        ComputeBudgetProgramId,
        SystemProgramId,
    };

    public static void Assert(
        IEnumerable<GuardedInstruction> instructions,
        string userPublicKey,
        string expectedInputMint,
        string quotedInputMint,
        string quotedOutputMint,
        ulong plannedAmount,
        ulong quotedInAmount)
    {
        var list = instructions.ToList();

        if (list.Count == 0)
            throw new NightfallException("Jupiter returned no swap instructions — nothing to sign.");

        // 1. The quote has to be for the asset and the size we planned. Checked first because a
        //    mismatch here means we are about to move the wrong thing, whatever the instructions say.
        if (!string.Equals(quotedInputMint, expectedInputMint, StringComparison.Ordinal))
            throw new NightfallException(
                $"The quote's input mint is {quotedInputMint}, but this tool is configured to sell {expectedInputMint}.");

        if (!string.Equals(quotedOutputMint, TokenAmount.WrappedSolMint, StringComparison.Ordinal))
            throw new NightfallException(
                $"The quote's output mint is {quotedOutputMint}, but a sell must pay out SOL ({TokenAmount.WrappedSolMint}).");

        if (quotedInAmount != plannedAmount)
            throw new NightfallException(
                $"The quote is for {quotedInAmount} base units but the plan approved {plannedAmount}.");

        // 2. Nothing may run that we do not recognise.
        foreach (var instruction in list)
        {
            if (!AllowedProgramIds.Contains(instruction.ProgramId))
                throw new NightfallException(
                    $"Refusing to sign: the swap calls an unexpected program {instruction.ProgramId}.");

            // ⚠️ ComputeBudget is exempt from the wallet check below, and ONLY ComputeBudget.
            // It sets the transaction's compute-unit limit and price and takes NO ACCOUNTS AT ALL, so
            // "must involve this wallet" is unsatisfiable for it by construction — Jupiter puts one on
            // every swap, and requiring it refused every live sell with a message that read like a
            // security incident. An instruction that names no accounts also cannot touch anyone's
            // funds: a Solana program may only read or write the accounts it is handed.
            //
            // The exemption is deliberately keyed on the program id rather than on "declares no
            // accounts" — the latter would be a far wider hole than the bug it closes.
            if (instruction.ProgramId == ComputeBudgetProgramId)
                continue;

            // 3. Every instruction must touch this wallet. An instruction that does not is either
            //    operating on somebody else's accounts or is not part of our swap at all.
            if (!instruction.Accounts.Contains(userPublicKey, StringComparer.Ordinal))
                throw new NightfallException(
                    $"Refusing to sign: an instruction for {instruction.ProgramId} does not involve this wallet.");
        }

        // 4. And it has to actually be a swap.
        if (list.All(i => i.ProgramId != JupiterAggregatorV6ProgramId))
            throw new NightfallException("Refusing to sign: no instruction calls the Jupiter aggregator, so this is not a swap.");
    }
}
