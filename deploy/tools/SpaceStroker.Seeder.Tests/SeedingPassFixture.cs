using System.Net;
using System.Text;

namespace SpaceStroker.Seeder.Tests;

/// <summary>
/// Records every outgoing request and replies from a canned route table. No socket is ever opened —
/// this is the same "pure, no network" rule the rest of the suite follows, just applied one layer
/// further out so the URLs and headers themselves can be asserted.
/// </summary>
public sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> _route;

    public RecordingHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> route) => _route = route;

    public List<RecordedRequest> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(new RecordedRequest(
            request.Method.Method,
            request.RequestUri!.ToString(),
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase)));

        var (status, body) = _route(request);

        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}

public sealed record RecordedRequest(string Method, string Url, Dictionary<string, string> Headers);

/// <summary>Canned response bodies in the API's real wire shape.</summary>
public static class Payloads
{
    public static string ActiveTypes(Guid gameTypeId, string name) => $$"""
    {
      "data": [
        {
          "gameTypeId": "{{gameTypeId}}",
          "gameCharacterType": 2,
          "name": "{{name}}",
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

    /// <summary>A round that already carries a bet — the pass stops before any money can move.</summary>
    public static string RoundWithABet(Guid gameId) => $$"""
    {
      "data": {
        "gameId": "{{gameId}}",
        "status": 1,
        "bets": [ { "choice": "abcdef", "amount": 1000.0 } ]
      },
      "messages": []
    }
    """;

    public const string Empty = """{ "data": null, "messages": [] }""";
}
