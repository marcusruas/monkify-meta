namespace SpaceStroker.Seeder;

/// <summary>Whether to seed this game right now, and the human-readable reason either way.</summary>
public readonly record struct SeedVerdict(bool ShouldSeed, string Reason);

/// <summary>
/// The whole skip ladder in one pure function, so every branch is unit-testable without a network.
///
/// ORDER MATTERS, twice over:
///  - the STATUS check precedes the bet count, because "the round is running" is the useful fact
///    about a round that is both running and full;
///  - the BET COUNT check precedes the balance, because an already-seeded round is the ordinary
///    steady state (this app's own bet from the last pass is what makes it non-empty), and
///    reporting that as a funding problem would turn a routine no-op into a false alarm.
/// </summary>
public static class SeedDecision
{
    public static SeedVerdict Evaluate(string gameName, GameTypeDto? type, ActiveGameDto? game, decimal? balance)
    {
        if (type is null)
            return new(false, $"\"{gameName}\" was not found among the operator's active game types");

        if (type.PaymentMethod != PaymentMethods.SolanaToken)
            return new(false, $"\"{gameName}\" is not a SolanaToken game (paymentMethod={type.PaymentMethod})");

        if (string.IsNullOrWhiteSpace(type.TokenMintAddress)
            || string.IsNullOrWhiteSpace(type.TokenReceiverPublicKey)
            || type.TokenDecimals is null)
        {
            return new(false, $"\"{gameName}\" is missing its token block (mint/decimals/receiver)");
        }

        if (game is null)
            return new(false, $"\"{gameName}\" has no active round");

        if (!GameStatus.IsAcceptingBets(game.Status))
            return new(false, $"\"{gameName}\" round {game.GameId} is {GameStatus.Name(game.Status)}, not accepting bets");

        var betCount = game.Bets?.Count ?? 0;
        if (betCount > 0)
            return new(false, $"\"{gameName}\" round {game.GameId} already has {betCount} bet(s)");

        if (balance is null)
            return new(false, $"\"{gameName}\" token balance could not be read");

        if (balance < type.RequiredAmount)
            return new(false, $"\"{gameName}\" balance {balance} is below the stake {type.RequiredAmount}");

        return new(true, $"\"{gameName}\" round {game.GameId} is empty and accepting bets");
    }
}
