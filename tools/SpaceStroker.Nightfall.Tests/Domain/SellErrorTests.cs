using Shouldly;
using SpaceStroker.Nightfall.Domain;
using Xunit;

namespace SpaceStroker.Nightfall.Tests.Domain;

public class SellErrorTests
{
    [Fact]
    public void Rate_limiting_is_retryable()
    {
        // Jupiter's free tier and the RPC both 429 under a concurrent batch — that is the single
        // most common failure and it must never be terminal.
        SellError.Classify(429, "Too Many Requests", isTransport: false).ShouldBe(SellErrorClass.Retryable);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public void Server_errors_are_retryable(int status)
    {
        SellError.Classify(status, "upstream", isTransport: false).ShouldBe(SellErrorClass.Retryable);
    }

    [Fact]
    public void A_transport_failure_is_retryable()
    {
        SellError.Classify(null, "The SSL connection could not be established", isTransport: true)
            .ShouldBe(SellErrorClass.Retryable);
    }

    [Theory]
    [InlineData("Blockhash not found")]
    [InlineData("block height exceeded")]
    [InlineData("Transaction was not confirmed in 90.00 seconds")]
    public void An_expired_blockhash_or_confirmation_timeout_is_retryable(string message)
    {
        // The transaction may still land, so this must not be recorded as a definitive failure.
        SellError.Classify(null, message, isTransport: false).ShouldBe(SellErrorClass.Retryable);
    }

    [Theory]
    [InlineData("custom program error: 0x1771 (slippage tolerance exceeded)")]
    [InlineData("Could not find any route")]
    [InlineData("insufficient funds for the transaction")]
    public void A_business_rejection_is_terminal(string message)
    {
        // Retrying these just spends time and rate limit to reach the same answer.
        SellError.Classify(null, message, isTransport: false).ShouldBe(SellErrorClass.Terminal);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(404)]
    public void Any_other_4xx_is_terminal(int status)
    {
        SellError.Classify(status, "bad request", isTransport: false).ShouldBe(SellErrorClass.Terminal);
    }

    [Fact]
    public void An_unrecognised_failure_is_terminal_rather_than_retried_forever()
    {
        // Fail closed: an unknown error retried in a loop against a money path is worse than
        // stopping and showing the operator what happened.
        SellError.Classify(null, "something nobody has seen before", isTransport: false)
            .ShouldBe(SellErrorClass.Terminal);
    }
}

public class RealizedSolTests
{
    [Fact]
    public void Realized_sol_is_the_balance_delta_with_the_fee_added_back()
    {
        // The wallet pays the fee, so post-pre understates what the SALE actually paid.
        var realized = RealizedSol.From(
            preBalances: [1_000_000L, 50L],
            postBalances: [1_499_995L, 50L],
            fee: 5_000L,
            accountIndex: 0);

        realized.ShouldBe(504_995L);
    }

    [Fact]
    public void Handles_a_sale_that_produced_nothing()
    {
        RealizedSol.From([1_000_000L], [995_000L], fee: 5_000L, accountIndex: 0).ShouldBe(0L);
    }

    [Fact]
    public void Refuses_a_missing_account_index_rather_than_reading_as_zero()
    {
        // A silently-zero realized amount would misreport a successful sale as worthless.
        Should.Throw<NightfallException>(() => RealizedSol.From([1L], [1L], fee: 0L, accountIndex: 3));
    }

    [Fact]
    public void Refuses_mismatched_balance_arrays()
    {
        Should.Throw<NightfallException>(() => RealizedSol.From([1L, 2L], [1L], fee: 0L, accountIndex: 0));
    }
}
