using System.Text;
using System.Text.Json;

namespace SpaceStroker.Seeder;

/// <summary>One API call's outcome. Status is NULL when no response arrived at all.</summary>
public sealed record ApiCall<T>(bool Ok, int? Status, T? Value, string Error);

/// <summary>
/// Thin client over the endpoints the seeder uses. Every one of them already ships; this app adds
/// nothing to the backend.
///
/// Deliberately sends NO Origin header: /rpc and /solana/* run a server-side Origin allow-list
/// (OriginAllowList) that permits an ABSENT Origin — the non-browser caller case — but would 403
/// an unrecognised one.
///
/// It sends NO auth of any kind either. `remove-operators` deleted LaunchController and the
/// X-Launch-Token gate outright: every route below is public, and the bet's identity is the wallet
/// carried in the request body. Re-adding a token here would call a route that no longer exists.
/// </summary>
public sealed class SpaceStrokerApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public SpaceStrokerApiClient(string baseUrl)
        : this(baseUrl, new HttpClientHandler())
    {
    }

    /// <summary>
    /// Test seam. The seeder's whole contract with the backend is a set of URLs and headers, and the
    /// one outage this tool has had was exactly that contract going stale — so the request shape has
    /// to be assertable without a network.
    /// </summary>
    public SpaceStrokerApiClient(string baseUrl, HttpMessageHandler handler)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    public Task<ApiCall<List<GameTypeDto>>> GetActiveTypesAsync(CancellationToken ct)
        => SendAsync<List<GameTypeDto>>(
            () => new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/games/active-types"),
            wrapped: true, ct);

    public Task<ApiCall<ActiveGameDto>> GetActiveGameAsync(Guid gameTypeId, CancellationToken ct)
        => SendAsync<ActiveGameDto>(
            () => new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/games/{gameTypeId}"),
            wrapped: true, ct);

    /// <summary>
    /// GET /solana/balance — cached ~10s, restricted to ACTIVE registered mints, not launch-token
    /// gated.
    ///
    /// The uncached /balance/live route is deliberately NOT used: a 6-hourly pre-flight has no
    /// staleness problem, and that route carries the tightest per-IP rule in the app (20/min).
    /// </summary>
    public Task<ApiCall<TokenBalanceDto>> GetTokenBalanceAsync(string wallet, string mint, CancellationToken ct)
        => SendAsync<TokenBalanceDto>(
            () => new HttpRequestMessage(HttpMethod.Get,
                $"{_baseUrl}/solana/balance?wallet={Uri.EscapeDataString(wallet)}&mint={Uri.EscapeDataString(mint)}"),
            wrapped: true, ct);

    public Task<ApiCall<object>> PlaceBetAsync(Guid gameId, RegisterBetBody body, CancellationToken ct)
        => SendAsync<object>(
            () => new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/games/{gameId}/bets")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            },
            wrapped: true, ct);

    /// <summary>
    /// Sends one request and normalizes the outcome. A thrown HttpRequestException or a timeout
    /// becomes Status = null, which RetrySchedule.IsTransient treats as retryable — that is the
    /// single most common transient case, and a status-only classifier would miss it.
    /// </summary>
    private async Task<ApiCall<T>> SendAsync<T>(Func<HttpRequestMessage> build, bool wrapped, CancellationToken ct)
    {
        try
        {
            using var request = build();
            using var response = await _http.SendAsync(request, ct);
            var status = (int)response.StatusCode;
            var raw = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return new(false, status, default, DescribeFailure(status, raw, wrapped));

            if (string.IsNullOrWhiteSpace(raw))
                return new(true, status, default, "");

            if (!wrapped)
                return new(true, status, JsonSerializer.Deserialize<T>(raw, SeederConfig.JsonOpts), "");

            var envelope = JsonSerializer.Deserialize<ApiResult<T>>(raw, SeederConfig.JsonOpts);
            return new(true, status, envelope is null ? default : envelope.Data, "");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // No response at all: connection reset, DNS failure, or the client timeout.
            return new(false, null, default, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Surfaces the API's own messages VERBATIM. They are the deliberately vague, operator-safe
    /// strings (constraints.md §6) — never reinterpret or embellish them.
    /// </summary>
    private static string DescribeFailure(int status, string raw, bool wrapped)
    {
        if (!wrapped || string.IsNullOrWhiteSpace(raw))
            return $"HTTP {status}";

        try
        {
            var envelope = JsonSerializer.Deserialize<ApiResult<object>>(raw, SeederConfig.JsonOpts);
            var messages = envelope?.Messages?
                .Select(m => m.Content)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList();

            return messages is { Count: > 0 }
                ? $"HTTP {status}: {string.Join("; ", messages)}"
                : $"HTTP {status}";
        }
        catch (JsonException)
        {
            return $"HTTP {status}";
        }
    }

    public void Dispose() => _http.Dispose();
}
