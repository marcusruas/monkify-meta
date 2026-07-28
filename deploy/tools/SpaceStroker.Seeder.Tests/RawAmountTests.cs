using Shouldly;
using Xunit;

namespace SpaceStroker.Seeder.Tests;

public class RawAmountTests
{
    [Theory]
    [InlineData(1, 6, 1_000_000UL)]
    [InlineData(2, 6, 2_000_000UL)]
    [InlineData(0.5, 6, 500_000UL)]
    [InlineData(1, 9, 1_000_000_000UL)]
    [InlineData(1, 0, 1UL)]
    [InlineData(0, 6, 0UL)]
    public void Converts_a_ui_amount_to_base_units_exactly(decimal ui, int decimals, ulong expected)
    {
        SolanaTransferClient.ToRawAmount(ui, decimals).ShouldBe(expected);
    }

    [Fact]
    public void Refuses_an_amount_finer_than_the_mint_supports()
    {
        // Silently truncating 0.0000001 at 6 decimals would underpay the stake, and the bet would
        // then be rejected AFTER the transfer had already confirmed on chain.
        Should.Throw<SeederException>(() => SolanaTransferClient.ToRawAmount(0.0000001m, 6));
    }

    [Fact]
    public void Refuses_a_negative_amount()
    {
        Should.Throw<SeederException>(() => SolanaTransferClient.ToRawAmount(-1m, 6));
    }

    [Fact]
    public void Does_not_lose_precision_the_way_double_maths_would()
    {
        // 0.1 + 0.2 style drift: 1.1 at 9 decimals must be exactly 1_100_000_000, not ...999 or ...001.
        SolanaTransferClient.ToRawAmount(1.1m, 9).ShouldBe(1_100_000_000UL);
    }

    [Fact]
    public void Shortens_a_signature_for_logging_without_losing_its_ends()
    {
        var signature = new string('a', 88);

        var shortened = SolanaTransferClient.Short(signature);

        shortened.Length.ShouldBeLessThan(signature.Length);
        shortened.ShouldStartWith("aaaaaaaa");
    }

    [Fact]
    public void Leaves_an_already_short_signature_alone()
    {
        SolanaTransferClient.Short("abc").ShouldBe("abc");
    }
}
