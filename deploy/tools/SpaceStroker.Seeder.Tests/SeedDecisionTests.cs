using Shouldly;
using Xunit;

namespace SpaceStroker.Seeder.Tests;

public class SeedDecisionTests
{
    private static GameTypeDto Type(decimal stake = 1m)
        => new(
            GameTypeId: Guid.NewGuid(),
            Name: "Cosmic Sprint",
            RequiredAmount: stake,
            MinimumNumberOfPlayers: 2,
            ChoiceRequiredLength: 6,
            AcceptDuplicatedCharacters: true,
            GameCharacterType: 2,
            PresetChoices: new List<string>(),
            TokenMintAddress: "mint",
            TokenDecimals: 6,
            TokenReceiverPublicKey: "receiver");

    private static ActiveGameDto Game(int status = 1, int bets = 0)
        => new(
            Guid.NewGuid(),
            status,
            Enumerable.Range(0, bets).Select(i => new ActiveBetDto($"c{i}")).ToList());

    [Fact]
    public void Seeds_an_empty_waiting_round_with_enough_balance()
    {
        SeedDecision.Evaluate("Cosmic Sprint", Type(), Game(status: 1, bets: 0), balance: 5m)
            .ShouldSeed.ShouldBeTrue();
    }

    [Fact]
    public void Seeds_an_empty_game_starting_round()
    {
        // GameStarting still accepts bets (Game.GameAcceptingBets covers both).
        SeedDecision.Evaluate("Cosmic Sprint", Type(), Game(status: 2, bets: 0), 5m)
            .ShouldSeed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Skips_a_round_that_already_has_bets(int bets)
    {
        var verdict = SeedDecision.Evaluate("Cosmic Sprint", Type(), Game(status: 1, bets: bets), 5m);

        verdict.ShouldSeed.ShouldBeFalse();
        verdict.Reason.ShouldContain(bets.ToString());
    }

    [Fact]
    public void A_null_bets_collection_counts_as_empty()
    {
        var game = new ActiveGameDto(Guid.NewGuid(), 1, null);

        SeedDecision.Evaluate("Cosmic Sprint", Type(), game, 5m).ShouldSeed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(3, "InProgress")]
    [InlineData(4, "Ended")]
    [InlineData(7, "RewardForWinnersCompleted")]
    [InlineData(9, "GameEndedAbruptely")]
    public void Skips_a_round_that_is_not_accepting_bets(int status, string expectedName)
    {
        var verdict = SeedDecision.Evaluate("Cosmic Sprint", Type(), Game(status: status), 5m);

        verdict.ShouldSeed.ShouldBeFalse();
        verdict.Reason.ShouldContain(expectedName);
    }

    [Fact]
    public void Skips_when_there_is_no_active_round()
    {
        var verdict = SeedDecision.Evaluate("Cosmic Sprint", Type(), null, 5m);

        verdict.ShouldSeed.ShouldBeFalse();
        verdict.Reason.ShouldContain("no active round");
    }

    [Fact]
    public void Skips_when_the_game_type_was_not_found()
    {
        var verdict = SeedDecision.Evaluate("Cosmic Sprint", null, Game(), 5m);

        verdict.ShouldSeed.ShouldBeFalse();
        verdict.Reason.ShouldContain("not found");
    }

    [Fact]
    public void Skips_when_the_balance_is_below_the_stake()
    {
        var verdict = SeedDecision.Evaluate("Cosmic Sprint", Type(stake: 2m), Game(), balance: 1.5m);

        verdict.ShouldSeed.ShouldBeFalse();
        verdict.Reason.ShouldContain("balance");
    }

    [Fact]
    public void Seeds_when_the_balance_exactly_equals_the_stake()
    {
        SeedDecision.Evaluate("Cosmic Sprint", Type(stake: 2m), Game(), balance: 2m)
            .ShouldSeed.ShouldBeTrue();
    }

    [Fact]
    public void Skips_when_the_balance_could_not_be_read()
    {
        var verdict = SeedDecision.Evaluate("Cosmic Sprint", Type(), Game(), balance: null);

        verdict.ShouldSeed.ShouldBeFalse();
        verdict.Reason.ShouldContain("balance");
    }

    /// <summary>
    /// Since `remove-operators` there is no paymentMethod to gate on — every type is crypto — so a
    /// COMPLETE token block is the only signal that a type takes a paid on-chain bet. That makes
    /// this the check that stands in for the old "is it a SolanaToken game?" branch.
    /// </summary>
    [Theory]
    [InlineData(null, 6, "receiver")]
    [InlineData("mint", null, "receiver")]
    [InlineData("mint", 6, null)]
    public void Skips_when_the_token_block_is_incomplete(string? mint, int? decimals, string? receiver)
    {
        var type = Type() with
        {
            TokenMintAddress = mint,
            TokenDecimals = decimals,
            TokenReceiverPublicKey = receiver,
        };

        var verdict = SeedDecision.Evaluate("Cosmic Sprint", type, Game(), 5m);

        verdict.ShouldSeed.ShouldBeFalse();
        verdict.Reason.ShouldContain("token");
    }

    [Fact]
    public void The_bets_check_runs_before_the_balance_check()
    {
        // An already-seeded round is the ordinary steady state — the app's own bet is what makes it
        // non-empty. Reporting that as a funding problem would turn a no-op into a false alarm.
        var verdict = SeedDecision.Evaluate("Cosmic Sprint", Type(), Game(bets: 1), balance: 0m);

        verdict.Reason.ShouldContain("bet");
        verdict.Reason.ShouldNotContain("balance");
    }

    [Fact]
    public void The_status_check_runs_before_the_bets_check()
    {
        // A running round is "in progress", not "already has bets" — the status is the useful fact.
        var verdict = SeedDecision.Evaluate("Cosmic Sprint", Type(), Game(status: 3, bets: 2), 5m);

        verdict.Reason.ShouldContain("InProgress");
    }
}
