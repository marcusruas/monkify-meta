using System.Text.Json;
using System.Text.Json.Serialization;
using Solnet.Wallet;
using Solnet.Wallet.Utilities;

namespace SpaceStroker.Nightfall.Domain;

/// <summary>
/// One wallet in the pool. Unlike the funder's PoolEntry this one DOES carry the private key —
/// Nightfall signs, the funder only sends to addresses.
/// </summary>
/// <remarks>
/// ⚠️ Never log or serialize this record wholesale. Everything the browser sees goes through
/// Application/WalletView, which has no key member at all.
/// </remarks>
public sealed record PoolEntry(string Alias, string PublicKey, string PrivateKey);

/// <summary>JSON binding shape. Shared by secrets.nightfall.json's "wallets" array.</summary>
public sealed class RawPoolEntry
{
    [JsonPropertyName("Alias")] public string? Alias { get; set; }
    [JsonPropertyName("PublicKey")] public string? PublicKey { get; set; }
    [JsonPropertyName("PrivateKey")] public string? PrivateKey { get; set; }
}

public static class WalletPool
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Unknown members are ignored (the default). Do NOT set UnmappedMemberHandling.Disallow:
        // card #63's pool file carries SupplyPercentage and must load unchanged.
    };

    public static IReadOnlyList<PoolEntry> Parse(string json)
    {
        List<RawPoolEntry>? raw;
        try
        {
            raw = JsonSerializer.Deserialize<List<RawPoolEntry>>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new NightfallException($"Could not parse the wallet pool: {ex.Message}");
        }

        if (raw is null) throw new NightfallException("The wallet pool is empty or null.");

        return FromEntries(raw);
    }

    public static IReadOnlyList<PoolEntry> FromEntries(IEnumerable<RawPoolEntry> raw)
    {
        var result = new List<PoolEntry>();
        var index = 0;

        foreach (var entry in raw)
        {
            index++;
            var alias = string.IsNullOrWhiteSpace(entry.Alias) ? $"W{index:00}" : entry.Alias.Trim();
            result.Add(new PoolEntry(alias, (entry.PublicKey ?? "").Trim(), (entry.PrivateKey ?? "").Trim()));
        }

        return result;
    }

    /// <summary>
    /// ⚠️ Solnet's `new PublicKey(string)` is NOT validation — it happily wraps a truncated string.
    /// Selling from a mistyped address is unrecoverable, so the 32-byte decode is asserted here and
    /// at config load, before any run can start.
    /// </summary>
    public static void ValidateAddress(string address, string alias)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new NightfallException($"Wallet '{alias}' has no PublicKey.");

        byte[] decoded;
        try
        {
            decoded = Encoders.Base58.DecodeData(address);
        }
        catch (Exception)
        {
            throw new NightfallException($"Wallet '{alias}' has a PublicKey that is not valid base58: {address}");
        }

        if (decoded.Length != 32 || !PublicKey.IsValid(address))
            throw new NightfallException(
                $"Wallet '{alias}' has a PublicKey that is not a 32-byte Solana address ({decoded.Length} bytes): {address}");
    }

    /// <summary>
    /// Proves the configured private key actually controls the configured address. A mismatch here is
    /// otherwise only discovered as an opaque on-chain signature failure, mid-batch, after a quote.
    /// </summary>
    public static void ValidateKeyPair(PoolEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.PrivateKey))
            throw new NightfallException($"Wallet '{entry.Alias}' has no PrivateKey.");

        Account account;
        try
        {
            var secret = Encoders.Base58.DecodeData(entry.PrivateKey);

            // A Solana keypair export is 64 bytes (32-byte seed then 32-byte public key). Solnet's
            // Account(privateKey, publicKey) wants them as base58 strings, so hand it what we decoded.
            if (secret.Length != 64)
                throw new NightfallException(
                    $"Wallet '{entry.Alias}' has a PrivateKey of {secret.Length} bytes; a Solana keypair is 64.");

            account = new Account(entry.PrivateKey, entry.PublicKey);
        }
        catch (NightfallException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new NightfallException($"Wallet '{entry.Alias}' has an unreadable PrivateKey: {ex.Message}");
        }

        if (account.PublicKey.Key != entry.PublicKey)
            throw new NightfallException(
                $"Wallet '{entry.Alias}': the PrivateKey belongs to a different address than the PublicKey it is " +
                "configured with. Signing would produce an opaque on-chain failure at spend time.");
    }
}
