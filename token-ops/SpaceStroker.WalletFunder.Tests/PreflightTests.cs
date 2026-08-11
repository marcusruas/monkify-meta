using Shouldly;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests;

public class PreflightTests
{
    // Real, well-formed 32-byte base58 Solana addresses (the SPL Token and System program ids).
    private const string ValidA = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";
    private const string ValidB = "11111111111111111111111111111111";

    private static PreflightResult Check(IReadOnlyList<PoolEntry> pool, decimal available = 1000m)
        => Preflight.Check(pool, available, minAmountSol: 0.001m, feeHeadroomSol: 0.05m);

    private static List<PoolEntry> Pool(params (string addr, decimal sol)[] items)
        => items.Select((x, i) => new PoolEntry($"W{i + 1:00}", x.addr, x.sol)).ToList();

    [Fact]
    public void Accepts_a_valid_pool()
        => Check(Pool((ValidA, 1m), (ValidB, 2m))).Ok.ShouldBeTrue();

    [Fact]
    public void Empty_pool_is_flagged_as_empty_and_not_an_error()
    {
        var result = Check(new List<PoolEntry>());

        result.IsEmpty.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("not-a-real-address")]                              // invalid base58 alphabet
    [InlineData("")]                                                // missing
    [InlineData("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5D")]      // one char short => not 32 bytes
    public void Rejects_a_malformed_address(string address)
    {
        var result = Check(Pool((address, 1m)));

        result.Ok.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains("W01"));
    }

    [Fact]
    public void Rejects_a_duplicate_address()
    {
        var result = Check(Pool((ValidA, 1m), (ValidA, 2m)));

        result.Ok.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.0001)]   // below minAmountSol
    public void Rejects_a_bad_amount(decimal sol)
        => Check(Pool((ValidA, sol))).Ok.ShouldBeFalse();

    [Fact]
    public void Rejects_a_balance_that_does_not_cover_the_fee_headroom()
    {
        // Total is exactly 10 and the balance is exactly 10 — the 0.05 headroom makes it short.
        var result = Check(Pool((ValidA, 10m)), available: 10m);

        result.Ok.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains("balance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Accepts_a_balance_that_covers_total_plus_headroom()
        => Check(Pool((ValidA, 10m)), available: 10.05m).Ok.ShouldBeTrue();

    [Fact]
    public void Reports_every_problem_at_once_rather_than_the_first()
        => Check(Pool(("bad", 1m), (ValidA, -5m))).Errors.Count.ShouldBeGreaterThanOrEqualTo(2);
}
