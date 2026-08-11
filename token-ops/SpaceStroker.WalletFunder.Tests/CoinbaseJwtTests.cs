using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Shouldly;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests;

public class CoinbaseJwtTests
{
    private static (CoinbaseSection coinbase, Ed25519PublicKeyParameters pub) NewEd25519Key()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        var priv = (Ed25519PrivateKeyParameters)pair.Private;
        var pub = (Ed25519PublicKeyParameters)pair.Public;

        // CDP hands out seed ‖ publicKey, base64 — reproduce that exact 64-byte shape.
        var combined = priv.GetEncoded().Concat(pub.GetEncoded()).ToArray();
        combined.Length.ShouldBe(64);

        return (new CoinbaseSection
        {
            ApiKeyId = "organizations/o/apiKeys/k",
            ApiKeySecret = Convert.ToBase64String(combined),
            KeyAlgorithm = "ed25519",
        }, pub);
    }

    private static (string header, string payload, byte[] signature, byte[] signingInput) Split(string jwt)
    {
        var parts = jwt.Split('.');
        parts.Length.ShouldBe(3);

        static byte[] Decode(string segment)
        {
            var padded = segment.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            return Convert.FromBase64String(padded);
        }

        return (Encoding.UTF8.GetString(Decode(parts[0])),
                Encoding.UTF8.GetString(Decode(parts[1])),
                Decode(parts[2]),
                Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"));
    }

    [Fact]
    public void Produces_three_base64url_segments_with_no_padding()
    {
        var (coinbase, _) = NewEd25519Key();

        var jwt = CoinbaseJwt.Create(coinbase, "POST", "api.coinbase.com", "/v2/accounts/x/transactions",
            DateTimeOffset.UtcNow, "abc123");

        jwt.Split('.').Length.ShouldBe(3);
        jwt.ShouldNotContain("=");
        jwt.ShouldNotContain("+");
        jwt.ShouldNotContain("/");
    }

    [Fact]
    public void Header_carries_EdDSA_kid_and_nonce()
    {
        var (coinbase, _) = NewEd25519Key();

        var (header, _, _, _) = Split(CoinbaseJwt.Create(coinbase, "GET", "api.coinbase.com", "/v2/accounts",
            DateTimeOffset.UtcNow, "nonce-1"));

        using var doc = JsonDocument.Parse(header);
        doc.RootElement.GetProperty("alg").GetString().ShouldBe("EdDSA");
        doc.RootElement.GetProperty("typ").GetString().ShouldBe("JWT");
        doc.RootElement.GetProperty("kid").GetString().ShouldBe(coinbase.ApiKeyId);
        doc.RootElement.GetProperty("nonce").GetString().ShouldBe("nonce-1");
    }

    [Fact]
    public void Claims_carry_sub_iss_aud_and_a_two_minute_expiry()
    {
        var (coinbase, _) = NewEd25519Key();
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

        var (_, payload, _, _) = Split(CoinbaseJwt.Create(coinbase, "GET", "api.coinbase.com", "/v2/accounts", now, "n"));

        using var doc = JsonDocument.Parse(payload);
        doc.RootElement.GetProperty("sub").GetString().ShouldBe(coinbase.ApiKeyId);
        doc.RootElement.GetProperty("iss").GetString().ShouldBe("cdp");
        doc.RootElement.GetProperty("aud")[0].GetString().ShouldBe("cdp_service");
        doc.RootElement.GetProperty("nbf").GetInt64().ShouldBe(1_700_000_000);
        doc.RootElement.GetProperty("exp").GetInt64().ShouldBe(1_700_000_120);
    }

    [Fact]
    public void Uri_claim_is_METHOD_HOST_PATH_with_no_scheme_or_query()
    {
        var (coinbase, _) = NewEd25519Key();

        var (_, payload, _, _) = Split(CoinbaseJwt.Create(coinbase, "POST", "api.coinbase.com",
            "/v2/accounts/abc/transactions", DateTimeOffset.UtcNow, "n"));

        using var doc = JsonDocument.Parse(payload);
        doc.RootElement.GetProperty("uri").GetString()
            .ShouldBe("POST api.coinbase.com/v2/accounts/abc/transactions");
    }

    [Fact]
    public void Signature_verifies_against_the_public_key()
    {
        var (coinbase, pub) = NewEd25519Key();

        var (_, _, signature, signingInput) = Split(CoinbaseJwt.Create(coinbase, "GET", "api.coinbase.com",
            "/v2/accounts", DateTimeOffset.UtcNow, "n"));

        var verifier = new Ed25519Signer();
        verifier.Init(false, pub);
        verifier.BlockUpdate(signingInput, 0, signingInput.Length);
        verifier.VerifySignature(signature).ShouldBeTrue();
    }

    [Fact]
    public void Rejects_a_secret_that_is_not_a_valid_ed25519_key()
    {
        var coinbase = new CoinbaseSection
        {
            ApiKeyId = "o/k",
            ApiKeySecret = Convert.ToBase64String(new byte[10]),
            KeyAlgorithm = "ed25519",
        };

        Should.Throw<FunderException>(
            () => CoinbaseJwt.Create(coinbase, "GET", "h", "/p", DateTimeOffset.UtcNow, "n"));
    }
}
