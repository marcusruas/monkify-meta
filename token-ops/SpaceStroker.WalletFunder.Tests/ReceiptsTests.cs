using Shouldly;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests;

public class ReceiptsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"receipts-{Guid.NewGuid():N}.json");

    private static FundingPlan Plan(params (string addr, decimal sol)[] items)
        => FundingPlan.Build(items.Select((x, i) => new PoolEntry($"W{i + 1:00}", x.addr, x.sol)).ToList());

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Creates_a_file_seeded_from_the_plan()
    {
        var plan = Plan(("AAA", 1m), ("BBB", 2m));

        var receipts = Receipts.LoadOrCreate(_path, plan, forcePlanChange: false);

        receipts.Outstanding(plan).Count.ShouldBe(2);
        File.Exists(_path).ShouldBeTrue();
    }

    [Fact]
    public void Skips_a_confirmed_wallet_on_resume()
    {
        var plan = Plan(("AAA", 1m), ("BBB", 2m));
        var first = Receipts.LoadOrCreate(_path, plan, false);
        first.Update(first.For(plan.Transfers[0]), e =>
        {
            e.Status = TransferStatus.Confirmed;
            e.ChainConfirmed = true;
        });

        var resumed = Receipts.LoadOrCreate(_path, plan, false);

        resumed.Outstanding(plan).Select(t => t.Address).ShouldBe(new[] { "BBB" });
    }

    [Fact]
    public void Retries_a_failed_wallet_on_resume()
    {
        var plan = Plan(("AAA", 1m));
        var first = Receipts.LoadOrCreate(_path, plan, false);
        first.Update(first.For(plan.Transfers[0]), e =>
        {
            e.Status = TransferStatus.Failed;
            e.Error = "boom";
        });

        Receipts.LoadOrCreate(_path, plan, false).Outstanding(plan).Count.ShouldBe(1);
    }

    [Fact]
    public void An_entry_holding_a_transaction_id_is_never_re_sent()
    {
        var plan = Plan(("AAA", 1m));
        var receipts = Receipts.LoadOrCreate(_path, plan, false);
        receipts.Update(receipts.For(plan.Transfers[0]), e =>
        {
            e.Status = TransferStatus.Pending;
            e.CoinbaseTransactionId = "tx-1";
        });

        Receipts.NeedsSend(receipts.For(plan.Transfers[0])).ShouldBeFalse();
    }

    [Fact]
    public void A_planned_entry_still_needs_a_send()
    {
        var plan = Plan(("AAA", 1m));
        var receipts = Receipts.LoadOrCreate(_path, plan, false);

        Receipts.NeedsSend(receipts.For(plan.Transfers[0])).ShouldBeTrue();
    }

    [Fact]
    public void Refuses_to_resume_when_the_plan_drifted()
    {
        Receipts.LoadOrCreate(_path, Plan(("AAA", 1m)), false);

        var ex = Should.Throw<FunderException>(() => Receipts.LoadOrCreate(_path, Plan(("AAA", 2m)), false));

        ex.Message.ShouldContain("--force-plan-change");
    }

    [Fact]
    public void Force_plan_change_re_keys_the_file()
    {
        Receipts.LoadOrCreate(_path, Plan(("AAA", 1m)), false);
        var changed = Plan(("AAA", 2m));

        var receipts = Receipts.LoadOrCreate(_path, changed, forcePlanChange: true);

        receipts.Outstanding(changed).Count.ShouldBe(1);
        Should.NotThrow(() => Receipts.LoadOrCreate(_path, changed, false));   // now matches
    }

    [Fact]
    public void Round_trips_every_field_and_stores_no_credential()
    {
        var plan = Plan(("AAA", 1.25m));
        var receipts = Receipts.LoadOrCreate(_path, plan, false);
        receipts.Update(receipts.For(plan.Transfers[0]), e =>
        {
            e.Status = TransferStatus.Confirmed;
            e.CoinbaseTransactionId = "tx-9";
            e.ChainConfirmed = true;
        });

        var reloaded = Receipts.LoadOrCreate(_path, plan, false).For(plan.Transfers[0]);

        reloaded.CoinbaseTransactionId.ShouldBe("tx-9");
        reloaded.AmountSol.ShouldBe(1.25m);
        reloaded.Label.ShouldBe("W01");
        reloaded.ChainConfirmed.ShouldBeTrue();

        var raw = File.ReadAllText(_path);
        raw.ShouldNotContain("apiKeySecret");
        raw.ShouldNotContain("PrivateKey");
    }
}
