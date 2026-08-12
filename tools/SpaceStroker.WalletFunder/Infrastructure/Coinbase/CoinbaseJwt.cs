using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using SpaceStroker.WalletFunder.Domain;

namespace SpaceStroker.WalletFunder.Infrastructure.Coinbase;

/// <summary>
/// Builds the short-lived CDP bearer token. The JWS is hand-rolled because .NET 8 has NO Ed25519 in the
/// BCL and Microsoft.IdentityModel will not sign EdDSA without a custom crypto provider.
/// A fresh token is minted per request: they live ~2 minutes and the `uri` claim binds method + path.
/// </summary>
public static class CoinbaseJwt
{
    private const int ExpirySeconds = 120;

    public static string Create(
        CoinbaseSection coinbase, string method, string host, string path, DateTimeOffset now, string nonce)
    {
        var algorithm = coinbase.KeyAlgorithm.Equals("ecdsa", StringComparison.OrdinalIgnoreCase) ? "ES256" : "EdDSA";

        var header = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["alg"] = algorithm,
            ["typ"] = "JWT",
            ["kid"] = coinbase.ApiKeyId,
            ["nonce"] = nonce,
        });

        var issued = now.ToUnixTimeSeconds();
        var claims = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["sub"] = coinbase.ApiKeyId,
            ["iss"] = "cdp",
            ["aud"] = new[] { "cdp_service" },
            ["nbf"] = issued,
            ["exp"] = issued + ExpirySeconds,
            ["uri"] = $"{method.ToUpperInvariant()} {host}{path}",
        });

        var signingInput = $"{Base64Url(header)}.{Base64Url(claims)}";
        var signature = algorithm == "EdDSA"
            ? SignEd25519(coinbase.ApiKeySecret, signingInput)
            : SignEs256(coinbase.ApiKeySecret, signingInput);

        return $"{signingInput}.{Base64Url(signature)}";
    }

    private static byte[] SignEd25519(string secret, string signingInput)
    {
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(secret);
        }
        catch (FormatException)
        {
            throw new FunderException("coinbase.apiKeySecret is not valid base64 for an ed25519 key.");
        }

        // CDP hands out 64 bytes: 32-byte seed ‖ 32-byte public key. Ed25519PrivateKeyParameters wants
        // the FIRST 32 ONLY — passing all 64 throws, and the wrong 32 yields a token Coinbase rejects.
        if (decoded.Length is not (32 or 64))
            throw new FunderException(
                $"coinbase.apiKeySecret decodes to {decoded.Length} bytes; an ed25519 key is 32 or 64.");

        var privateKey = new Ed25519PrivateKeyParameters(decoded, 0);
        var bytes = Encoding.ASCII.GetBytes(signingInput);

        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(bytes, 0, bytes.Length);
        return signer.GenerateSignature();
    }

    private static byte[] SignEs256(string pem, string signingInput)
    {
        using var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportFromPem(pem);
        }
        catch (Exception ex)
        {
            throw new FunderException($"coinbase.apiKeySecret is not a valid EC private key PEM: {ex.Message}");
        }

        // JWS requires raw r‖s. The .NET default is DER, which every JWT verifier rejects.
        return ecdsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    public static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
