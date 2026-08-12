using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SpaceStroker.WalletFunder.Domain;

namespace SpaceStroker.WalletFunder.Infrastructure.Coinbase;

public sealed record CoinbaseAccount(string Id, string Currency, decimal Balance);

public sealed record SendResult(string TransactionId, string Status);

/// <summary>
/// Auth/scope failure — fatal for the WHOLE run rather than for one wallet: a revoked key, a missing
/// wallet:transactions:send scope or a destination blocked by address allowlisting would fail every
/// remaining wallet identically, so retrying per wallet just burns time.
/// </summary>
public sealed class CoinbaseAuthException(string message) : Exception(message);

public sealed class CoinbaseClient : IDisposable
{
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(20),
    ];

    private readonly CoinbaseSection _coinbase;
    private readonly TravelRuleSection? _travelRule;
    private readonly HttpClient _http;

    public CoinbaseClient(CoinbaseSection coinbase, TravelRuleSection? travelRule = null)
    {
        _coinbase = coinbase;
        _travelRule = travelRule;
        _http = new HttpClient
        {
            BaseAddress = new Uri($"https://{coinbase.ApiHost}"),
            Timeout = TimeSpan.FromSeconds(60),
        };
    }

    public async Task<CoinbaseAccount> ResolveSolAccountAsync(string? accountIdOverride, CancellationToken ct)
    {
        var accounts = new List<CoinbaseAccount>();
        string? path = "/v2/accounts?limit=100";

        while (path is not null)
        {
            using var doc = await SendJsonAsync(HttpMethod.Get, path, body: null, ct);
            var root = doc.RootElement;

            foreach (var item in root.GetProperty("data").EnumerateArray())
            {
                var balance = item.GetProperty("balance");
                var currency = balance.GetProperty("currency").GetString() ?? "";
                var amount = decimal.Parse(balance.GetProperty("amount").GetString() ?? "0",
                    CultureInfo.InvariantCulture);

                accounts.Add(new CoinbaseAccount(item.GetProperty("id").GetString() ?? "", currency, amount));
            }

            path = root.TryGetProperty("pagination", out var page)
                   && page.TryGetProperty("next_uri", out var next)
                   && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
        }

        if (!string.IsNullOrWhiteSpace(accountIdOverride))
            return accounts.FirstOrDefault(a => a.Id == accountIdOverride)
                   ?? throw new FunderException($"Coinbase account '{accountIdOverride}' was not found on this key.");

        var sol = accounts.Where(a => a.Currency.Equals("SOL", StringComparison.OrdinalIgnoreCase)).ToList();
        return sol.Count switch
        {
            1 => sol[0],
            0 => throw new FunderException(
                "No SOL account found on this Coinbase key. Add coinbase.accountId to secrets.json."),
            _ => throw new FunderException(
                $"{sol.Count} SOL accounts found — set coinbase.accountId in secrets.json to disambiguate."),
        };
    }

    public async Task<SendResult> SendAsync(
        string accountId, string toAddress, decimal sol, Guid idem, CancellationToken ct)
    {
        var body = CoinbaseSendBody.Build(toAddress, sol, idem, _travelRule);

        using var doc = await SendJsonAsync(HttpMethod.Post, $"/v2/accounts/{accountId}/transactions", body, ct);
        var data = doc.RootElement.GetProperty("data");

        return new SendResult(data.GetProperty("id").GetString() ?? "", data.GetProperty("status").GetString() ?? "");
    }

    public async Task<string> GetTransactionStatusAsync(string accountId, string transactionId, CancellationToken ct)
    {
        using var doc = await SendJsonAsync(
            HttpMethod.Get, $"/v2/accounts/{accountId}/transactions/{transactionId}", null, ct);

        return doc.RootElement.GetProperty("data").GetProperty("status").GetString() ?? "";
    }

    /// <summary>
    /// Retries transport errors, 429 and 5xx only. Retrying the send POST is SAFE because the request
    /// carries its deterministic idem — Coinbase deduplicates it. Any other 4xx is terminal;
    /// 401/403 escalates to CoinbaseAuthException so the orchestrator aborts the entire run.
    /// </summary>
    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var uriPath = path.Split('?')[0];       // the uri claim excludes the query string
        Exception? last = null;

        for (var attempt = 0; attempt <= Backoff.Length; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);
            var jwt = CoinbaseJwt.Create(_coinbase, method.Method, _coinbase.ApiHost, uriPath,
                DateTimeOffset.UtcNow, RandomNonce());

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            request.Headers.TryAddWithoutValidation("CB-VERSION", _coinbase.ApiVersion);

            if (body is not null)
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct);
            }
            catch (HttpRequestException ex) when (attempt < Backoff.Length)
            {
                last = ex;
                await Task.Delay(Jitter(Backoff[attempt]), ct);
                continue;
            }

            using (response)
            {
                var payload = await response.Content.ReadAsStringAsync(ct);

                if (response.IsSuccessStatusCode) return JsonDocument.Parse(payload);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new CoinbaseAuthException(
                        $"Coinbase rejected the request ({(int)response.StatusCode}). Check that the API key carries " +
                        $"the 'wallet:transactions:send' scope, and that the destination is allowlisted if address " +
                        $"allowlisting is enabled on the account. Key: {MaskKeyId(_coinbase.ApiKeyId)}. " +
                        $"Response: {Truncate(payload)}");

                var retryable = response.StatusCode is HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                if (!retryable || attempt == Backoff.Length)
                    throw new FunderException(
                        $"Coinbase {method.Method} {uriPath} failed ({(int)response.StatusCode}): {Truncate(payload)}");

                last = new FunderException($"Coinbase returned {(int)response.StatusCode}.");
                await Task.Delay(Jitter(Backoff[attempt]), ct);
            }
        }

        throw new FunderException($"Coinbase {method.Method} {uriPath} failed after retries: {last?.Message}");
    }

    private static string RandomNonce()
    {
        var bytes = new byte[16];
        Random.Shared.NextBytes(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static TimeSpan Jitter(TimeSpan baseDelay)
        => baseDelay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));

    private static string Truncate(string value) => value.Length <= 400 ? value : value[..400] + "…";

    /// <summary>Key ids may be printed MASKED; the secret and the minted JWT never are.</summary>
    public static string MaskKeyId(string apiKeyId)
    {
        if (string.IsNullOrWhiteSpace(apiKeyId)) return "(unset)";

        var tail = apiKeyId.Split('/').LastOrDefault() ?? apiKeyId;
        return tail.Length <= 4 ? "****" : $"****{tail[^4..]}";
    }

    public void Dispose() => _http.Dispose();
}
