using Shouldly;
using Xunit;
using SpaceStroker.WalletFunder.Application;
using SpaceStroker.WalletFunder.Domain;
using SpaceStroker.WalletFunder.Infrastructure;

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

    [Fact]
    public void Says_plainly_when_no_travel_rule_data_will_be_sent()
        => ConsoleReport.DescribeTravelRule(null).ShouldBe("none — travel_rule_data will NOT be sent");

    [Fact]
    public void Says_plainly_when_the_travel_rule_block_is_switched_off()
        => ConsoleReport.DescribeTravelRule(new TravelRuleSection())
            .ShouldBe("none — travel_rule_data will NOT be sent");

    [Fact]
    public void Echoes_the_exact_travel_rule_payload_that_will_be_sent()
    {
        // Built through CoinbaseSendBody so the printed line can never drift from the real request —
        // "what am I about to send?" must be answerable before money moves.
        var description = ConsoleReport.DescribeTravelRule(new TravelRuleSection
        {
            BeneficiaryWalletType = "WALLET_TYPE_SELF_HOSTED",
            IsSelf = "IS_SELF_TRUE",
            BeneficiaryName = "Satoshi Nakamoto",
            BeneficiaryCountry = "BR",
        });

        description.ShouldBe(
            "{\"beneficiary_wallet_type\":\"WALLET_TYPE_SELF_HOSTED\",\"is_self\":\"IS_SELF_TRUE\"," +
            "\"beneficiary_name\":\"Satoshi Nakamoto\",\"beneficiary_address\":{\"country\":\"BR\"}}");
    }
}
