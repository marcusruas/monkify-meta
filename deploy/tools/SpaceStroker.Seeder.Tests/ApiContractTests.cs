using System.Text.Json;
using Shouldly;
using Xunit;

namespace SpaceStroker.Seeder.Tests;

/// <summary>
/// Pins the seeder's DTOs to the shape the API ACTUALLY returns today.
///
/// This file exists because of a real outage: `remove-operators` deleted `LaunchController` and
/// dropped `PaymentMethod` from `GameParametersDto`, and the seeder — whose only contract with the
/// backend is these records — kept compiling and kept passing its own tests while silently seeding
/// nothing. Every payload below is copied from the live response shape, not invented.
/// </summary>
public class ApiContractTests
{
    /// <summary>
    /// GET /games/active-types, exactly as it comes back from `main` today. NOTE what is absent:
    /// there is no `paymentMethod` — the operator removal made every game type crypto, so the field
    /// was dropped from the DTO. A seeder that still gates on it skips every game forever.
    /// </summary>
    private const string ActiveTypesPayload = """
    {
      "data": [
        {
          "gameTypeId": "7c3f1b12-9d0e-4a55-9f2c-1a2b3c4d5e6f",
          "gameCharacterType": 2,
          "name": "Cosmic Sprint",
          "description": "Type the sequence",
          "requiredAmount": 1000.0,
          "minimumNumberOfPlayers": 2,
          "choiceRequiredLength": 6,
          "acceptDuplicatedCharacters": true,
          "allowFreeBet": false,
          "allowOrbit": true,
          "commissionPercentage": 1.0,
          "tokenSymbol": "STROKE",
          "tokenDecimals": 6,
          "tokenMintAddress": "MintAddress111111111111111111111111111111111",
          "tokenReceiverPublicKey": "Receiver1111111111111111111111111111111111",
          "presetChoices": [],
          "lotteryParameters": null
        }
      ],
      "messages": []
    }
    """;

    private static GameTypeDto ParseFirstType()
    {
        var envelope = JsonSerializer.Deserialize<ApiResult<List<GameTypeDto>>>(
            ActiveTypesPayload, SeederConfig.JsonOpts);

        envelope.ShouldNotBeNull();
        envelope!.Data.ShouldNotBeNull();
        return envelope.Data!.Single();
    }

    [Fact]
    public void The_live_active_types_payload_still_carries_everything_the_seeder_reads()
    {
        var type = ParseFirstType();

        type.Name.ShouldBe("Cosmic Sprint");
        type.RequiredAmount.ShouldBe(1000.0m);
        type.ChoiceRequiredLength.ShouldBe(6);
        type.GameCharacterType.ShouldBe(2);
        type.AcceptDuplicatedCharacters.ShouldBeTrue();
        type.TokenMintAddress.ShouldNotBeNullOrWhiteSpace();
        type.TokenDecimals.ShouldBe(6);
        type.TokenReceiverPublicKey.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The regression itself. A type parsed from the CURRENT payload must be seedable; before the
    /// fix it resolved paymentMethod to the 0 default and was rejected as "not a SolanaToken game".
    /// </summary>
    [Fact]
    public void A_type_parsed_from_the_live_payload_is_seedable()
    {
        var type = ParseFirstType();
        var round = new ActiveGameDto(Guid.NewGuid(), GameStatus.WaitingBets, new List<ActiveBetDto>());

        var verdict = SeedDecision.Evaluate("Cosmic Sprint", type, round, balance: 5000m);

        verdict.ShouldSeed.ShouldBeTrue(verdict.Reason);
    }
}
