using Shouldly;
using Xunit;
using SpaceStroker.WalletFunder.Application;
using SpaceStroker.WalletFunder.Domain;

namespace SpaceStroker.WalletFunder.Tests.Application;

public class ConsoleReportTests
{
    [Fact]
    public void Shortens_a_long_address_but_keeps_both_ends_verifiable()
        => ConsoleReport.ShortAddress("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA")
            .ShouldBe("Tokenk…VQ5DA");

    [Fact]
    public void Leaves_a_short_address_alone()
        => ConsoleReport.ShortAddress("ABC").ShouldBe("ABC");

    [Fact]
    public void Confirms_when_the_typed_total_matches()
        => ConsoleReport.ConfirmTotal(12.75m, 3, new StringReader("12.75")).ShouldBeTrue();

    [Fact]
    public void Tolerates_surrounding_whitespace()
        => ConsoleReport.ConfirmTotal(12.75m, 3, new StringReader("  12.75  ")).ShouldBeTrue();

    [Theory]
    [InlineData("12.7")]
    [InlineData("yes")]
    [InlineData("")]
    [InlineData("12,75")]   // comma decimal is NOT accepted — the prompt prints the invariant form
    public void Refuses_anything_that_is_not_the_exact_total(string typed)
        => ConsoleReport.ConfirmTotal(12.75m, 3, new StringReader(typed)).ShouldBeFalse();
}
