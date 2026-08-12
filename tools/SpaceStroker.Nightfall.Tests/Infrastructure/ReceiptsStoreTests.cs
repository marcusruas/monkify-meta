using Shouldly;
using SpaceStroker.Nightfall.Domain;
using SpaceStroker.Nightfall.Infrastructure;
using Xunit;

namespace SpaceStroker.Nightfall.Tests.Infrastructure;

public class ReceiptsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "nightfall-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_directory, "nightfall-receipts.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static SellReceipt Receipt(string alias = "W01") => new()
    {
        Alias = alias,
        PublicKey = $"pk-{alias}",
        BatchId = "b1",
    };

    [Fact]
    public void Creates_the_directory_it_needs()
    {
        var store = ReceiptsStore.LoadOrCreate(Path_, "mint");

        store.Record(Receipt());

        File.Exists(Path_).ShouldBeTrue();
    }

    [Fact]
    public void Rewrites_the_file_on_every_state_change_never_only_at_the_end()
    {
        // A crash mid-batch must not lose a signature — the file IS the audit trail, so it is
        // written after each change rather than batched to the end of the run.
        var store = ReceiptsStore.LoadOrCreate(Path_, "mint");
        var receipt = Receipt();

        store.Record(receipt);
        var afterPlanned = File.ReadAllText(Path_);

        receipt.MoveTo(SellStatus.Quoted);
        receipt.QuotedSol = 1.5m;
        store.Record(receipt);
        var afterQuoted = File.ReadAllText(Path_);

        receipt.MoveTo(SellStatus.Sent);
        receipt.Signature = "the-signature";
        store.Record(receipt);
        var afterSent = File.ReadAllText(Path_);

        afterPlanned.ShouldNotBe(afterQuoted);
        afterQuoted.ShouldNotBe(afterSent);
        afterSent.ShouldContain("the-signature");
    }

    [Fact]
    public void Round_trips_through_disk()
    {
        var store = ReceiptsStore.LoadOrCreate(Path_, "mint");
        var receipt = Receipt();
        receipt.MoveTo(SellStatus.Quoted);
        receipt.QuotedSol = 2.25m;
        store.Record(receipt);

        var reloaded = ReceiptsStore.LoadOrCreate(Path_, "mint");

        reloaded.Entries.Count.ShouldBe(1);
        reloaded.Entries[0].Alias.ShouldBe("W01");
        reloaded.Entries[0].QuotedSol.ShouldBe(2.25m);
    }

    [Fact]
    public void Keeps_a_row_per_wallet()
    {
        var store = ReceiptsStore.LoadOrCreate(Path_, "mint");

        store.Record(Receipt("W01"));
        store.Record(Receipt("W02"));

        store.Entries.Count.ShouldBe(2);
    }

    [Fact]
    public void Never_writes_a_private_key()
    {
        // AC27. The receipt type has no key member at all; this asserts the shape stays that way.
        var store = ReceiptsStore.LoadOrCreate(Path_, "mint");
        store.Record(Receipt());

        File.ReadAllText(Path_).ToLowerInvariant().ShouldNotContain("privatekey");
    }
}
