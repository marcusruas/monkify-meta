using System.Net;
using Shouldly;
using Solnet.Wallet.Utilities;
using Xunit;

namespace SpaceStroker.Seeder.Tests;

/// <summary>
/// Pins the HTTP shape of one discovery pass. These assertions are what would have caught the
/// operator removal: `LaunchController` was deleted, so `POST /launch-token/default` began returning
/// 404, the pass aborted before reading anything, and nothing but a live run revealed it.
///
/// No socket is opened — every response is canned (see <see cref="RecordingHandler"/>).
/// </summary>
public class SeedingPassTests : IDisposable
{
    private const string BaseUrl = "https://api.example.test";

    private readonly List<string> _temp = new();

    private static readonly string SyntheticKey =
        new Base58Encoder().EncodeData(Enumerable.Range(1, 64).Select(i => (byte)i).ToArray());

    public void Dispose()
    {
        foreach (var path in _temp)
            if (File.Exists(path)) File.Delete(path);
    }

    private SeederConfig Config()
    {
        var path = Path.Combine(Path.GetTempPath(), $"seeder-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, $$"""
        {
          "apiBaseUrl": "{{BaseUrl}}",
          "walletPrivateKeyBase58": "{{SyntheticKey}}",
          "displayName": "Edson",
          "intervalHours": 6,
          "retryAttempts": 0,
          "retryBaseDelaySeconds": 1,
          "confirmTimeoutSeconds": 5
        }
        """);
        _temp.Add(path);
        return SeederConfig.Load(path);
    }

    /// <summary>
    /// Routes the two reads a discovery pass makes. Every target round already carries a bet, so the
    /// pass always stops before the balance read and can never reach the chain client.
    /// </summary>
    private static RecordingHandler Handler(Guid gameTypeId, string name)
        => new(request =>
        {
            var url = request.RequestUri!.AbsolutePath;

            if (url.EndsWith("/games/active-types", StringComparison.Ordinal))
                return (HttpStatusCode.OK, Payloads.ActiveTypes(gameTypeId, name));

            if (url.Contains($"/games/{gameTypeId}", StringComparison.Ordinal))
                return (HttpStatusCode.OK, Payloads.RoundWithABet(Guid.NewGuid()));

            // Anything else is a route this tool should no longer be calling at all.
            return (HttpStatusCode.NotFound, Payloads.Empty);
        });

    private (RecordingHandler Handler, SeedingPass Pass) Build(Guid gameTypeId, string name)
    {
        var config = Config();
        var handler = Handler(gameTypeId, name);
        var api = new SpaceStrokerApiClient(config.ApiBaseUrl, handler);
        var chain = new SolanaTransferClient($"{config.ApiBaseUrl}/rpc");

        return (handler, new SeedingPass(config, api, chain, new BetSeeds(), new Random(1)));
    }

    [Fact]
    public async Task A_pass_never_calls_the_deleted_launch_token_endpoint()
    {
        var (handler, pass) = Build(Guid.NewGuid(), "Cosmic Sprint");

        await pass.RunAsync(CancellationToken.None);

        handler.Requests.ShouldNotBeEmpty();
        handler.Requests.ShouldAllBe(r => !r.Url.Contains("launch-token"));
    }

    [Fact]
    public async Task A_pass_sends_no_launch_token_header()
    {
        // Nothing reads X-Launch-Token any more; identity is the wallet on the bet body.
        var (handler, pass) = Build(Guid.NewGuid(), "Cosmic Sprint");

        await pass.RunAsync(CancellationToken.None);

        handler.Requests.ShouldAllBe(r => !r.Headers.ContainsKey("X-Launch-Token"));
    }

    [Fact]
    public async Task A_pass_reads_the_active_types_and_then_each_target_round()
    {
        var gameTypeId = Guid.NewGuid();
        var (handler, pass) = Build(gameTypeId, "Cosmic Sprint");

        await pass.RunAsync(CancellationToken.None);

        // Discovery happens once; the round read happens per target that resolved by name. Only
        // "Cosmic Sprint" is in the canned catalogue, so "Beast Mode" resolves to nothing and skips.
        handler.Requests.Count(r => r.Url.EndsWith("/games/active-types")).ShouldBe(1);
        handler.Requests.Count(r => r.Url.Contains($"/games/{gameTypeId}")).ShouldBe(1);
    }

    [Fact]
    public async Task Resolving_targets_returns_the_catalogue_without_minting_anything()
    {
        var (handler, pass) = Build(Guid.NewGuid(), "Cosmic Sprint");

        var types = await pass.ResolveTargetsAsync(CancellationToken.None);

        types.ShouldNotBeNull();
        types!.ShouldNotBeEmpty();
        SeedingPass.FindByName(types, "Cosmic Sprint").ShouldNotBeNull();
        handler.Requests.Count.ShouldBe(1);
    }
}
