using Solnet.Wallet;
using SpaceStroker.Nightfall.Domain;

namespace SpaceStroker.Nightfall.Infrastructure.Solana;

/// <summary>
/// Signs a wire transaction WITHOUT parsing its message.
///
/// ⚠️ This is the resolution of the spec's open question (§12 finding 1). Solnet 6.1.0 exposes only
/// legacy Message/Transaction/TransactionBuilder — there is no VersionedTransaction, no MessageV0 and
/// no address-lookup-table support (verified against the package's own XML docs). Jupiter routes
/// routinely need lookup tables, so a locally-assembled legacy transaction cannot carry them.
///
/// The way through is that a Solana signature covers the MESSAGE BYTES, and the wire format puts the
/// signature array in front of them:
///
///     [shortvec signature count][64-byte signature] * count [ ...message bytes... ]
///
/// So we can take Jupiter's ready v0 transaction, sign the bytes after the signature array, and write
/// our signature into slot 0 — correct for legacy and v0 alike, because we never interpret the
/// message. What we DO interpret is the instruction list from /swap-instructions, which
/// <see cref="SwapInstructionsGuard"/> asserts before we get here. So "assert what we sign" holds:
/// the guard vets the semantics, this vets the shape.
/// </summary>
public static class TransactionSigner
{
    private const int SignatureLength = 64;

    public static byte[] Sign(byte[] wireTransaction, Account signer)
    {
        if (wireTransaction.Length < 1 + SignatureLength)
            throw new NightfallException(
                $"The transaction Jupiter returned is {wireTransaction.Length} bytes — too short to hold a signature.");

        // The signature count is a shortvec (compact-u16). A swap we pay for ourselves needs exactly
        // one signer, so the count must be a single byte holding 1. Anything else means the
        // transaction expects a co-signer we do not have, and signing it would produce a transaction
        // that can never land — better to refuse loudly than to broadcast something incomplete.
        var count = wireTransaction[0];

        if (count != 1)
            throw new NightfallException(
                $"The transaction expects {count} signatures; Nightfall signs single-signer swaps only.");

        var messageOffset = 1 + SignatureLength;

        if (wireTransaction.Length <= messageOffset)
            throw new NightfallException("The transaction Jupiter returned carries no message to sign.");

        var message = new byte[wireTransaction.Length - messageOffset];
        Buffer.BlockCopy(wireTransaction, messageOffset, message, 0, message.Length);

        var signature = signer.Sign(message);

        if (signature.Length != SignatureLength)
            throw new NightfallException($"Produced a {signature.Length}-byte signature; expected {SignatureLength}.");

        // Copy so a caller holding the original bytes never sees them mutate underneath.
        var signed = (byte[])wireTransaction.Clone();
        Buffer.BlockCopy(signature, 0, signed, 1, SignatureLength);

        return signed;
    }

    /// <summary>The message bytes a signature covers — exposed so tests can verify the signature.</summary>
    public static byte[] MessageOf(byte[] wireTransaction)
    {
        var messageOffset = 1 + SignatureLength;
        var message = new byte[wireTransaction.Length - messageOffset];
        Buffer.BlockCopy(wireTransaction, messageOffset, message, 0, message.Length);
        return message;
    }
}
