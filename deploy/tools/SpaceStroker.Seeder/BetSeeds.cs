using System.Security.Cryptography;
using System.Text;

namespace SpaceStroker.Seeder;

/// <summary>
/// Per-bet provably-fair seeds, mirroring the shipped Stake-style model
/// (features/provably-fair.md): Bet.Seed = HMAC-SHA256(clientSeed, nonce), computed client-side,
/// with the raw client seed never leaving the client.
///
/// HMAC-SHA256 is 32 bytes = exactly 64 hex characters, which matches the API's [MaxLength(64)]
/// on Seed with no truncation.
///
/// The client seed is per-process and deliberately not persisted — a seeded bet stays verifiable
/// for the life of the run, which is all this tool needs.
/// </summary>
public sealed class BetSeeds
{
    private readonly byte[] _clientSeed;
    private long _nonce;

    public BetSeeds() : this(RandomNumberGenerator.GetBytes(32)) { }

    public BetSeeds(byte[] clientSeed) => _clientSeed = clientSeed;

    public string Next(out long nonce)
    {
        nonce = _nonce++;
        return Compute(_clientSeed, nonce);
    }

    public static string Compute(byte[] clientSeed, long nonce)
    {
        var hash = HMACSHA256.HashData(clientSeed, Encoding.UTF8.GetBytes(nonce.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
