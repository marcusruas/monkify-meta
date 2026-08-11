using Shouldly;
using SpaceStroker.Nightfall.Domain;
using Xunit;

namespace SpaceStroker.Nightfall.Tests.Domain;

public class SellReceiptTests
{
    private static SellReceipt New() => new()
    {
        Alias = "W01",
        PublicKey = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA",
        BatchId = "b1",
    };

    [Theory]
    [InlineData(SellStatus.Planned, SellStatus.Quoted)]
    [InlineData(SellStatus.Planned, SellStatus.Failed)]
    [InlineData(SellStatus.Quoted, SellStatus.Simulated)]
    [InlineData(SellStatus.Quoted, SellStatus.Sent)]
    [InlineData(SellStatus.Quoted, SellStatus.Failed)]
    [InlineData(SellStatus.Sent, SellStatus.Sold)]
    [InlineData(SellStatus.Sent, SellStatus.Unconfirmed)]
    [InlineData(SellStatus.Sent, SellStatus.Failed)]
    public void Allows_the_legal_transitions(SellStatus from, SellStatus to)
    {
        var receipt = New();
        receipt.ForceStatus(from);

        Should.NotThrow(() => receipt.MoveTo(to));
        receipt.Status.ShouldBe(to);
    }

    [Theory]
    [InlineData(SellStatus.Sold, SellStatus.Sent)]
    [InlineData(SellStatus.Sold, SellStatus.Quoted)]
    [InlineData(SellStatus.Failed, SellStatus.Sold)]
    [InlineData(SellStatus.Planned, SellStatus.Sold)]
    public void Refuses_an_illegal_transition(SellStatus from, SellStatus to)
    {
        // A settled sale must never be walked backwards: the receipt is the audit trail, and a
        // Sold row that reverts to Sent would invite a second sale of the same position.
        var receipt = New();
        receipt.ForceStatus(from);

        Should.Throw<NightfallException>(() => receipt.MoveTo(to));
    }

    [Fact]
    public void A_settled_sale_keeps_both_the_quoted_and_the_realized_amount()
    {
        // The gap between them IS the slippage a concurrent batch paid — losing either hides it.
        var receipt = New();
        receipt.MoveTo(SellStatus.Quoted);
        receipt.QuotedSol = 1.284m;
        receipt.MoveTo(SellStatus.Sent);
        receipt.Signature = "sig";
        receipt.MoveTo(SellStatus.Sold);
        receipt.RealizedSol = 1.271m;

        receipt.QuotedSol.ShouldBe(1.284m);
        receipt.RealizedSol.ShouldBe(1.271m);
        receipt.Signature.ShouldBe("sig");
    }

    [Fact]
    public void Upserting_the_same_wallet_replaces_its_row_rather_than_appending()
    {
        var document = new ReceiptsDocument { TokenMint = "mint" };
        var first = New();
        first.MoveTo(SellStatus.Quoted);
        document.Upsert(first);

        var second = New();
        second.MoveTo(SellStatus.Failed);
        document.Upsert(second);

        document.Entries.Count.ShouldBe(1);
        document.Entries[0].Status.ShouldBe(SellStatus.Failed);
    }
}
