using Shouldly;
using SpaceStroker.WalletFunder.Domain;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests.Domain;

public class LegacyReceiptsTests
{
    private const string Current = "/tools/Receipts/receipts.json";
    private const string Legacy = "/tools/receipts.json";

    [Fact]
    public void A_legacy_file_with_no_current_one_is_reported()
    {
        // THE money case. Each wallet's Coinbase idem is derived from the plan, so a receipts file
        // the tool cannot see reads as "no prior run" — and a resend is a REAL second transfer.
        var message = LegacyReceipts.Describe(currentExists: false, legacyExists: true, Current, Legacy);

        message.ShouldNotBeNull();
        message.ShouldContain(Legacy);
        message.ShouldContain(Current);
    }

    [Fact]
    public void A_current_file_wins_even_when_a_legacy_one_lingers()
    {
        LegacyReceipts.Describe(currentExists: true, legacyExists: true, Current, Legacy).ShouldBeNull();
    }

    [Fact]
    public void A_first_run_is_silent()
    {
        LegacyReceipts.Describe(currentExists: false, legacyExists: false, Current, Legacy).ShouldBeNull();
    }

    [Fact]
    public void A_current_file_alone_is_silent()
    {
        LegacyReceipts.Describe(currentExists: true, legacyExists: false, Current, Legacy).ShouldBeNull();
    }
}
