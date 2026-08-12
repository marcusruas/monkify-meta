using Solnet.Wallet;

namespace SpaceStroker.TokenSniper.Infrastructure.Solana;

/// <summary>One instruction as the RPC returns it, reduced to the three fields this reader needs.</summary>
public sealed record TraceInstruction(int ProgramIdIndex, IReadOnlyList<int> AccountIndexes, byte[] Data);

/// <summary>
/// Reads <c>bonding_curve_v2</c> — the deployed program's 17th buy account — off a real transaction.
///
/// ⚠️ It is NOT derived. The published IDL declares 16 accounts for <c>buy</c>; the deployed program
/// reads two more and rejects a 16-account buy with BuybackFeeRecipientMissing (6062). The second of
/// those is a <c>global.buyback_fee_recipients</c> entry, but the first is a per-mint PDA whose seeds
/// are not published — every const seed in the on-chain IDL was tried against four live buys and none
/// reproduces it. Rather than guess, we do what this tool already does for the account list itself and
/// read the value from a transaction the chain has accepted. Every pump.fun token has at least one:
/// the creator's dev buy at launch.
/// </summary>
public static class BuyTrace
{
    /// <summary>Position of <c>bonding_curve_v2</c>: immediately after the 16 the IDL declares.</summary>
    public const int BondingCurveV2Index = 16;

    public static PublicKey? FindBondingCurveV2(
        IReadOnlyList<string> accountKeys, IEnumerable<TraceInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.ProgramIdIndex < 0 || instruction.ProgramIdIndex >= accountKeys.Count) continue;
            if (accountKeys[instruction.ProgramIdIndex] != PumpFunProgram.ProgramId.Key) continue;

            if (instruction.Data.Length < PumpFunProgram.BuyDiscriminator.Length) continue;
            if (!instruction.Data.AsSpan(0, PumpFunProgram.BuyDiscriminator.Length)
                    .SequenceEqual(PumpFunProgram.BuyDiscriminator)) continue;

            // A buy carrying only the IDL's 16 predates the trailing pair. Returning key 15 instead
            // would put a wrong-but-present account into a real buy, so this reports nothing.
            if (instruction.AccountIndexes.Count <= BondingCurveV2Index) continue;

            var index = instruction.AccountIndexes[BondingCurveV2Index];
            if (index < 0 || index >= accountKeys.Count) continue;

            return new PublicKey(accountKeys[index]);
        }

        return null;
    }
}
