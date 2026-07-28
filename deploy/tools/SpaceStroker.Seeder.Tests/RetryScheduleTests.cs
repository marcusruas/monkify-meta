using Shouldly;
using Xunit;

namespace SpaceStroker.Seeder.Tests;

public class RetryScheduleTests
{
    [Theory]
    [InlineData(null)]   // no response at all — connection reset / DNS / timeout
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public void Transient_failures_are_retryable(int? status)
    {
        RetrySchedule.IsTransient(status).ShouldBeTrue();
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public void Terminal_failures_are_not_retryable(int status)
    {
        RetrySchedule.IsTransient(status).ShouldBeFalse();
    }

    [Fact]
    public void A_missing_response_is_transient()
    {
        // The load-bearing case: Cloud Armor and a dropped connection both surface as "no response",
        // and a status-only classifier would never retry either.
        RetrySchedule.IsTransient(null).ShouldBeTrue();
    }

    [Fact]
    public void Delays_are_exponential_and_within_the_jitter_band()
    {
        var delays = RetrySchedule.Delays(attempts: 3, baseDelaySeconds: 5, new Random(1));

        delays.Count.ShouldBe(3);

        double[] centres = { 5, 10, 20 };
        for (var i = 0; i < delays.Count; i++)
        {
            delays[i].TotalSeconds.ShouldBeGreaterThanOrEqualTo(centres[i] * 0.8);
            delays[i].TotalSeconds.ShouldBeLessThanOrEqualTo(centres[i] * 1.2);
        }
    }

    [Fact]
    public void Zero_attempts_produces_no_delays()
    {
        RetrySchedule.Delays(0, 5, new Random(1)).ShouldBeEmpty();
    }

    [Fact]
    public void A_negative_attempt_count_produces_no_delays_rather_than_throwing()
    {
        RetrySchedule.Delays(-3, 5, new Random(1)).ShouldBeEmpty();
    }

    [Fact]
    public void Jitter_makes_two_different_seeds_diverge()
    {
        var a = RetrySchedule.Delays(3, 5, new Random(1));
        var b = RetrySchedule.Delays(3, 5, new Random(999));

        a.SequenceEqual(b).ShouldBeFalse();
    }

    [Fact]
    public void The_same_seed_reproduces_the_same_schedule()
    {
        RetrySchedule.Delays(3, 5, new Random(7))
            .SequenceEqual(RetrySchedule.Delays(3, 5, new Random(7)))
            .ShouldBeTrue();
    }

    [Fact]
    public void Everything_before_the_transfer_is_retryable()
    {
        RetrySchedule.IsRetryable(RetryPhase.PreSend, SeederOperation.Discovery).ShouldBeTrue();
        RetrySchedule.IsRetryable(RetryPhase.PreSend, SeederOperation.SendTransfer).ShouldBeTrue();
    }

    [Fact]
    public void THE_MONEY_RULE_a_sent_transfer_is_never_retryable()
    {
        // Once a signature exists the stake is gone. Re-sending would spend a SECOND stake for one
        // bet, and nothing downstream could tell the difference.
        RetrySchedule.IsRetryable(RetryPhase.PostSend, SeederOperation.SendTransfer).ShouldBeFalse();
    }

    [Fact]
    public void Confirming_and_placing_the_bet_stay_retryable_after_the_send()
    {
        // Safe: polling is read-only, and re-POSTing the SAME signature is rejected by the backend
        // as PaymentSignatureHasBeenUsed.
        RetrySchedule.IsRetryable(RetryPhase.PostSend, SeederOperation.ConfirmSignature).ShouldBeTrue();
        RetrySchedule.IsRetryable(RetryPhase.PostSend, SeederOperation.PlaceBet).ShouldBeTrue();
    }

    [Fact]
    public void Discovery_is_meaningless_after_the_send_and_is_refused()
    {
        RetrySchedule.IsRetryable(RetryPhase.PostSend, SeederOperation.Discovery).ShouldBeFalse();
    }
}
