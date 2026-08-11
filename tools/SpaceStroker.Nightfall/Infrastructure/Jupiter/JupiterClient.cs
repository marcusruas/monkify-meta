using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using SpaceStroker.Nightfall.Domain;

namespace SpaceStroker.Nightfall.Infrastructure.Jupiter;

/// <summary>
/// Jupiter, used for ROUTE DATA ONLY. It never sees a private key and never touches the chain —
/// every transaction it helps build is signed locally and broadcast through our own RPC.
/// </summary>
public sealed class JupiterClient(HttpClient http, JupiterSection settings)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private string BaseUrl => settings.ApiBaseUrl.TrimEnd('/');

    public async Task<JupiterQuote> QuoteAsync(string inputMint, ulong amount, CancellationToken ct)
    {
        var url = $"{BaseUrl}/quote"
                  + $"?inputMint={Uri.EscapeDataString(inputMint)}"
                  + $"&outputMint={Uri.EscapeDataString(TokenAmount.WrappedSolMint)}"
                  + $"&amount={amount}"
                  + $"&slippageBps={settings.SlippageBps}"
                  + "&restrictIntermediateTokens=true";

        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), "quote", ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement.Clone();

        if (!root.TryGetProperty("outAmount", out _))
            throw new NightfallException("Jupiter returned no route for this amount.");

        return new JupiterQuote(
            InputMint: ReadString(root, "inputMint"),
            OutputMint: ReadString(root, "outputMint"),
            InAmount: ReadUlong(root, "inAmount"),
            OutAmount: ReadUlong(root, "outAmount"),
            PriceImpactPercent: ReadPriceImpactPercent(root),
            Raw: root);
    }

    /// <summary>
    /// The instruction list, fetched purely so <see cref="SwapInstructionsGuard"/> can vet it. We do
    /// not assemble from it — Solnet 6.1.0 cannot build a versioned transaction with address lookup
    /// tables (see TransactionSigner) — but inspecting it is what lets us refuse a swap that is not
    /// the one we asked for, BEFORE a signature exists.
    /// </summary>
    public async Task<IReadOnlyList<GuardedInstruction>> SwapInstructionsAsync(
        JupiterQuote quote, string userPublicKey, CancellationToken ct)
    {
        var body = SwapBody(quote, userPublicKey);

        using var response = await SendAsync(
            () => Post($"{BaseUrl}/swap-instructions", body), "swap-instructions", ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(json);

        var instructions = new List<GuardedInstruction>();

        foreach (var name in (string[])["computeBudgetInstructions", "setupInstructions", "cleanupInstruction", "swapInstruction"])
        {
            if (!document.RootElement.TryGetProperty(name, out var node)) continue;

            if (node.ValueKind == JsonValueKind.Array)
                instructions.AddRange(node.EnumerateArray().Select(Read));
            else if (node.ValueKind == JsonValueKind.Object)
                instructions.Add(Read(node));
        }

        return instructions;
    }

    /// <summary>The ready v0 wire transaction. Signed locally by TransactionSigner, never by Jupiter.</summary>
    public async Task<byte[]> SwapTransactionAsync(JupiterQuote quote, string userPublicKey, CancellationToken ct)
    {
        using var response = await SendAsync(
            () => Post($"{BaseUrl}/swap", SwapBody(quote, userPublicKey)), "swap", ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("swapTransaction", out var encoded))
            throw new NightfallException("Jupiter's swap response carried no transaction.");

        return Convert.FromBase64String(encoded.GetString()
            ?? throw new NightfallException("Jupiter's swap transaction was empty."));
    }

    private string SwapBody(JupiterQuote quote, string userPublicKey)
    {
        // wrapAndUnwrapSol is what makes the proceeds land as NATIVE SOL in the wallet instead of as
        // wrapped SOL in a leftover account the operator would then have to clean up by hand.
        var payload = new StringBuilder();
        payload.Append('{');
        payload.Append("\"quoteResponse\":").Append(quote.Raw.GetRawText()).Append(',');
        payload.Append("\"userPublicKey\":\"").Append(userPublicKey).Append("\",");
        payload.Append("\"wrapAndUnwrapSol\":true,");
        payload.Append("\"dynamicComputeUnitLimit\":true,");
        payload.Append("\"prioritizationFeeLamports\":").Append(settings.PrioritizationFeeLamports);
        payload.Append('}');

        return payload.ToString();
    }

    private static HttpRequestMessage Post(string url, string body) => new(HttpMethod.Post, url)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    /// <summary>
    /// Retries only what SellError calls retryable, with exponential backoff and jitter — the same
    /// shape SolanaService.BuildPolicy uses in monkify-backend.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> request, string operation, CancellationToken ct)
    {
        const int maxAttempts = 4;

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            string? failure;
            int? status = null;
            var transport = false;

            try
            {
                response = await http.SendAsync(request(), ct);

                if (response.IsSuccessStatusCode)
                    return response;

                status = (int)response.StatusCode;
                var detail = await response.Content.ReadAsStringAsync(ct);
                failure = $"Jupiter {operation} failed with HTTP {status}: {Truncate(detail)}";
                response.Dispose();
            }
            catch (HttpRequestException ex)
            {
                transport = true;
                failure = $"Jupiter {operation} could not be reached: {ex.Message}";
                response?.Dispose();
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                transport = true;
                failure = $"Jupiter {operation} timed out: {ex.Message}";
                response?.Dispose();
            }

            var classification = SellError.Classify(status, failure, transport);

            if (classification == SellErrorClass.Terminal || attempt == maxAttempts)
                throw new NightfallException(failure);

            var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt - 1) * 500 + Random.Shared.Next(0, 250));
            await Task.Delay(delay, ct);
        }
    }

    private static GuardedInstruction Read(JsonElement instruction)
    {
        var accounts = instruction.TryGetProperty("accounts", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray()
                  .Select(a => a.TryGetProperty("pubkey", out var pk) ? pk.GetString() ?? "" : "")
                  .ToList()
            : [];

        return new GuardedInstruction(
            ReadString(instruction, "programId"),
            accounts,
            instruction.TryGetProperty("data", out var data) ? data.GetString() ?? "" : "");
    }

    private static string ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

    private static ulong ReadUlong(JsonElement element, string name)
    {
        // Jupiter sends amounts as JSON STRINGS — they exceed what a JS number holds safely.
        var raw = ReadString(element, name);

        return ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new NightfallException($"Jupiter returned an unreadable {name}: '{raw}'.");
    }

    private static decimal ReadPriceImpactPercent(JsonElement root)
    {
        // priceImpactPct is a FRACTION ("0.0123" = 1.23%), not a percentage. Treating it as a
        // percentage would make the price-impact abort ~100x too permissive — the guard would
        // effectively never fire, which is the failure mode it exists to prevent.
        var raw = ReadString(root, "priceImpactPct");

        if (string.IsNullOrWhiteSpace(raw))
            return 0m;

        return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var fraction)
            ? Math.Abs(fraction) * 100m
            : throw new NightfallException($"Jupiter returned an unreadable priceImpactPct: '{raw}'.");
    }

    private static string Truncate(string value)
        => value.Length <= 300 ? value : value[..300] + "…";
}
