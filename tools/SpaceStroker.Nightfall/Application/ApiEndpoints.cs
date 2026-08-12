using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SpaceStroker.Nightfall.Domain;
using SpaceStroker.Nightfall.Infrastructure;
using SpaceStroker.Nightfall.Infrastructure.Jupiter;

namespace SpaceStroker.Nightfall.Application;

public sealed record SellRequest(string[] Wallets, decimal? Percent);
public sealed record ModeRequest(bool Live);
public sealed record QuoteView(decimal TokenAmount, string TokenAmountRaw, decimal OutSol, decimal PriceImpactPercent);

/// <summary>
/// The localhost surface the dashboard calls.
///
/// ⚠️ Every response here is built from public data only — WalletView and QuoteView have no key
/// members, so no serializer setting or future refactor can leak one to the browser.
/// </summary>
public static class ApiEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Map(
        WebApplication app,
        NightfallState state,
        SecretsFile secrets,
        JupiterClient jupiter,
        QuoteCache quotes,
        SellRunner runner,
        PollLoop poll,
        EventStream events)
    {
        app.MapGet("/api/state", () => Results.Json(state.Snapshot(), Json));

        app.MapGet("/api/quote", async (string wallet, decimal? percent, CancellationToken ct) =>
        {
            var entry = state.Find(wallet);
            if (entry is null) return Results.NotFound(new { error = "Unknown wallet." });

            try
            {
                var balance = state.BalanceOf(wallet);
                var amount = SellPlan.AmountFor(balance.TokenRaw, percent);

                // Keyed on the AMOUNT, not the wallet: two wallets holding the same position share
                // one quote, which matters when a hover sweeps across the pool.
                var quote = await quotes.GetOrAddAsync(
                    $"{secrets.Nightfall.Token.Mint}:{amount}:{secrets.Nightfall.Jupiter.SlippageBps}",
                    () => jupiter.QuoteAsync(secrets.Nightfall.Token.Mint, amount, ct));

                return Results.Json(new QuoteView(
                    TokenAmount.ToUi(amount, state.Mint.Decimals),
                    amount.ToString(),
                    quote.OutSol,
                    quote.PriceImpactPercent), Json);
            }
            catch (NightfallException ex)
            {
                return Results.Json(new { error = ex.Message }, Json, statusCode: 400);
            }
        });

        app.MapPost("/api/mode", (ModeRequest request) =>
        {
            state.SetLive(request.Live);
            ConsoleReport.Mode(request.Live);
            events.Publish("mode", new { live = request.Live });

            return Results.Json(new { live = state.IsLive }, Json);
        });

        app.MapPost("/api/sell", async (SellRequest request, CancellationToken ct) =>
        {
            if (request.Wallets.Length == 0)
                return Results.Json(new { error = "No wallets selected." }, Json, statusCode: 400);

            if (request.Percent is not null and (<= 0m or > 100m))
                return Results.Json(new { error = "SELL % must be greater than 0 and at most 100." }, Json, statusCode: 400);

            var batchId = await runner.RunAsync(request.Wallets, request.Percent, ct);

            return Results.Json(new { batchId, live = state.IsLive }, Json);
        });

        app.MapPost("/api/refresh", async (CancellationToken ct) =>
        {
            await poll.RefreshAsync(ct);
            return Results.Json(state.Snapshot(), Json);
        });

        app.MapGet("/api/events", async (HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";

            var reader = events.Subscribe();

            try
            {
                await foreach (var message in reader.ReadAllAsync(ct))
                {
                    await context.Response.WriteAsync(message, ct);
                    await context.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                // The operator closed the tab.
            }
        });
    }
}

/// <summary>
/// Server-sent events to every open dashboard tab. One-way traffic, so a WebSocket library would be
/// a dependency bought for nothing.
/// </summary>
public sealed class EventStream
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly List<Channel<string>> _subscribers = [];

    // A plain object, not System.Threading.Lock — that type is .NET 9+ and this targets net8.0.
    private readonly object _gate = new();

    public ChannelReader<string> Subscribe()
    {
        var channel = Channel.CreateUnbounded<string>();

        lock (_gate) _subscribers.Add(channel);

        return channel.Reader;
    }

    public void Publish(string type, object payload)
    {
        var message = $"event: {type}\ndata: {JsonSerializer.Serialize(payload, Json)}\n\n";

        lock (_gate)
        {
            // A closed tab's channel simply stops accepting; drop it rather than growing the list.
            _subscribers.RemoveAll(channel => !channel.Writer.TryWrite(message));
        }
    }
}
