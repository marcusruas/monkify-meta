using System.Text.Json.Serialization;

namespace SpaceStroker.Seeder;

/// <summary>
/// The API's standard envelope (SpaceStroker.Results.ApiResult&lt;T&gt;). Every endpoint uses it
/// EXCEPT POST /launch-token/default, which returns a bare { "token": "..." } because
/// LaunchController extends ControllerBase rather than BaseController.
/// </summary>
public sealed record ApiResult<T>(
    [property: JsonPropertyName("data")] T? Data,
    [property: JsonPropertyName("messages")] List<ApiMessage>? Messages);

public sealed record ApiMessage(
    [property: JsonPropertyName("content")] string? Content);

public sealed record LaunchTokenResponse(
    [property: JsonPropertyName("token")] string? Token);

/// <summary>Mirrors SpaceStroker.Infrastructure.Contracts.Games.GameParametersDto.</summary>
public sealed record GameTypeDto(
    [property: JsonPropertyName("gameTypeId")] Guid GameTypeId,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("requiredAmount")] decimal RequiredAmount,
    [property: JsonPropertyName("minimumNumberOfPlayers")] int MinimumNumberOfPlayers,
    [property: JsonPropertyName("choiceRequiredLength")] int? ChoiceRequiredLength,
    [property: JsonPropertyName("acceptDuplicatedCharacters")] bool AcceptDuplicatedCharacters,
    [property: JsonPropertyName("gameCharacterType")] int GameCharacterType,
    [property: JsonPropertyName("paymentMethod")] int PaymentMethod,
    [property: JsonPropertyName("presetChoices")] List<string>? PresetChoices,
    [property: JsonPropertyName("tokenMintAddress")] string? TokenMintAddress,
    [property: JsonPropertyName("tokenDecimals")] int? TokenDecimals,
    [property: JsonPropertyName("tokenReceiverPublicKey")] string? TokenReceiverPublicKey);

/// <summary>Mirrors SpaceStroker.Infrastructure.Contracts.Games.ActiveGameDto (only what we read).</summary>
public sealed record ActiveGameDto(
    [property: JsonPropertyName("gameId")] Guid GameId,
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("bets")] List<ActiveBetDto>? Bets);

public sealed record ActiveBetDto(
    [property: JsonPropertyName("choice")] string? Choice);

/// <summary>Mirrors SpaceStroker.Infrastructure.Contracts.Solana.TokenBalanceDto.</summary>
public sealed record TokenBalanceDto(
    [property: JsonPropertyName("wallet")] string? Wallet,
    [property: JsonPropertyName("mint")] string? Mint,
    [property: JsonPropertyName("uiAmount")] decimal UiAmount,
    [property: JsonPropertyName("rawAmount")] string? RawAmount,
    [property: JsonPropertyName("decimals")] int Decimals);

/// <summary>Mirrors SpaceStroker.Infrastructure.Handlers.Games.RegisterBet.RegisterBetRequestBody.</summary>
public sealed record RegisterBetBody(
    [property: JsonPropertyName("paymentSignature")] string PaymentSignature,
    [property: JsonPropertyName("wallet")] string Wallet,
    [property: JsonPropertyName("externalPlayerId")] string? ExternalPlayerId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("choice")] string Choice,
    [property: JsonPropertyName("seed")] string Seed,
    [property: JsonPropertyName("nonce")] long Nonce);

/// <summary>GameStatus values (SpaceStroker.Domain.Games.ValueObjects.GameStatus).</summary>
public static class GameStatus
{
    public const int WaitingBets = 1;
    public const int GameStarting = 2;

    /// <summary>Mirrors Game.GameAcceptingBets — the only two statuses a bet can be placed on.</summary>
    public static bool IsAcceptingBets(int status) => status is WaitingBets or GameStarting;

    public static string Name(int status) => status switch
    {
        1 => "WaitingBets",
        2 => "GameStarting",
        3 => "InProgress",
        4 => "Ended",
        5 => "NotEnoughPlayersToStart",
        6 => "RewardForWinnersInProgress",
        7 => "RewardForWinnersCompleted",
        8 => "ErrorWhenProcessingRewards",
        9 => "GameEndedAbruptely",
        _ => $"#{status}",
    };
}

/// <summary>PaymentMethod values (SpaceStroker.Domain.Games.ValueObjects.PaymentMethod).</summary>
public static class PaymentMethods
{
    public const int Standard = 0;
    public const int SolanaToken = 1;
}
