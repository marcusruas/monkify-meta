using Shouldly;
using Xunit;

namespace SpaceStroker.Seeder.Tests;

public class BetSeedsTests
{
    private static readonly byte[] ClientSeed = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void A_seed_is_exactly_64_lowercase_hex_characters()
    {
        // The API's RegisterBetRequestBody caps Seed at [MaxLength(64)]; HMAC-SHA256 is 32 bytes,
        // which is exactly 64 hex characters, so nothing is ever truncated.
        var seed = BetSeeds.Compute(ClientSeed, 0);

        seed.Length.ShouldBe(64);
        seed.ShouldBe(seed.ToLowerInvariant());
        seed.ShouldAllBe(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
    }

    [Fact]
    public void The_same_client_seed_and_nonce_are_deterministic()
    {
        BetSeeds.Compute(ClientSeed, 7).ShouldBe(BetSeeds.Compute(ClientSeed, 7));
    }

    [Fact]
    public void A_different_nonce_produces_a_different_seed()
    {
        BetSeeds.Compute(ClientSeed, 1).ShouldNotBe(BetSeeds.Compute(ClientSeed, 2));
    }

    [Fact]
    public void A_different_client_seed_produces_a_different_seed()
    {
        var other = Enumerable.Range(100, 32).Select(i => (byte)i).ToArray();

        BetSeeds.Compute(ClientSeed, 1).ShouldNotBe(BetSeeds.Compute(other, 1));
    }

    [Fact]
    public void Next_increments_the_nonce_from_zero()
    {
        var seeds = new BetSeeds(ClientSeed);

        var first = seeds.Next(out var n0);
        var second = seeds.Next(out var n1);

        n0.ShouldBe(0);
        n1.ShouldBe(1);
        first.ShouldBe(BetSeeds.Compute(ClientSeed, 0));
        second.ShouldBe(BetSeeds.Compute(ClientSeed, 1));
    }

    [Fact]
    public void The_default_constructor_produces_a_usable_random_client_seed()
    {
        var a = new BetSeeds().Next(out _);
        var b = new BetSeeds().Next(out _);

        a.Length.ShouldBe(64);
        a.ShouldNotBe(b);
    }
}
