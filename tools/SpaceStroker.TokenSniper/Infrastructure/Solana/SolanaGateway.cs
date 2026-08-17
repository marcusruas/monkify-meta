using System.Text.Json;
using Solnet.Rpc;
using Solnet.Rpc.Builders;
using Solnet.Rpc.Models;
using Solnet.Rpc.Types;
using Solnet.Wallet;
using Solnet.Wallet.Utilities;
using SpaceStroker.TokenSniper.Domain;

namespace SpaceStroker.TokenSniper.Infrastructure.Solana;

public sealed record MintInfo(ulong TotalSupplyBaseUnits, int Decimals, PublicKey TokenProgram);

public sealed record SubmitResult(string? Signature, string? Error);

/// <summary>
/// Every RPC call the run makes. Kept behind one type so the Domain rules stay IO-free and the
/// key-bearing paths are all in one file.
/// ⚠️ A keyed endpoint is required in practice: the public one rate-limits (it 429'd during this
/// tool's own protocol research) and silently drops sends, which with ~40 real-SOL buys in an hour is
/// a money bug rather than an inconvenience.
/// </summary>
public sealed class SolanaGateway
{
    private readonly IRpcClient _client;

    private static readonly HttpClient Http = new();
    private readonly string _rpcUrl;

    public SolanaGateway(string rpcUrl)
    {
        if (string.IsNullOrWhiteSpace(rpcUrl))
            throw new SniperException("solana.rpcUrl is empty — a keyed RPC endpoint is required.");

        _client = ClientFactory.GetClient(rpcUrl);
        _rpcUrl = rpcUrl;
        MaskedEndpoint = MaskRpcUrl(rpcUrl);
    }

    /// <summary>What may be printed. The key never appears in output, receipts or reports.</summary>
    public string MaskedEndpoint { get; }

    public async Task<MintInfo> GetMintInfoAsync(PublicKey mint)
    {
        var account = await _client.GetAccountInfoAsync(mint.Key, Commitment.Confirmed);
        if (!account.WasSuccessful || account.Result?.Value is null)
            throw new SniperException($"Could not read the mint account {mint.Key}: {account.Reason}");

        var owner = account.Result.Value.Owner;
        if (owner != PumpFunProgram.TokenProgramId.Key && owner != PumpFunProgram.Token2022ProgramId.Key)
            throw new SniperException($"The mint {mint.Key} is owned by {owner}, which is not an SPL token program.");

        var supply = await _client.GetTokenSupplyAsync(mint.Key, Commitment.Confirmed);
        if (!supply.WasSuccessful || supply.Result?.Value is null)
            throw new SniperException($"Could not read the supply of {mint.Key}: {supply.Reason}");

        if (!ulong.TryParse(supply.Result.Value.Amount, out var amount))
            throw new SniperException($"The mint {mint.Key} reported an unparseable supply '{supply.Result.Value.Amount}'.");

        return new MintInfo(amount, supply.Result.Value.Decimals, new PublicKey(owner));
    }

    /// <summary>Null when the curve account does not exist — treated as "graduated or wrong mint".</summary>
    public async Task<CurveState?> GetCurveAsync(PublicKey curveAddress)
    {
        var data = await GetAccountDataAsync(curveAddress);
        return data is null ? null : AccountLayouts.DecodeBondingCurve(data);
    }

    public async Task<GlobalConfig> GetGlobalAsync()
    {
        var data = await GetAccountDataAsync(PumpFunProgram.DeriveGlobal())
                   ?? throw new SniperException("The pump.fun global account could not be read — is the RPC healthy?");

        return AccountLayouts.DecodeGlobal(data);
    }

    public async Task<ulong> GetLamportBalanceAsync(PublicKey wallet)
    {
        var balance = await _client.GetBalanceAsync(wallet.Key, Commitment.Confirmed);
        if (!balance.WasSuccessful || balance.Result is null)
            throw new SniperException($"Could not read the SOL balance of {wallet.Key}: {balance.Reason}");

        return balance.Result.Value;
    }

    /// <summary>
    /// The wallet's token balance in base units, 0 when the associated account does not exist yet.
    /// This is the RESUME AUTHORITY — Solana has no server-side idempotency token, so the balance,
    /// not the receipts file, decides whether a buy already landed.
    /// </summary>
    public async Task<ulong> GetTokenBalanceAsync(PublicKey associatedTokenAccount)
    {
        var balance = await _client.GetTokenAccountBalanceAsync(associatedTokenAccount.Key, Commitment.Confirmed);
        if (!balance.WasSuccessful || balance.Result?.Value is null) return 0;

        return ulong.TryParse(balance.Result.Value.Amount, out var amount) ? amount : 0;
    }

    public async Task<bool> AccountExistsAsync(PublicKey address) =>
        await GetAccountDataAsync(address) is not null;

    public async Task<SubmitResult> SubmitAsync(IReadOnlyList<TransactionInstruction> instructions, Account signer)
    {
        var blockhash = await _client.GetLatestBlockHashAsync(Commitment.Finalized);
        if (!blockhash.WasSuccessful || blockhash.Result?.Value is null)
            return new SubmitResult(null, $"could not fetch a blockhash: {blockhash.Reason}");

        var builder = new TransactionBuilder()
            .SetRecentBlockHash(blockhash.Result.Value.Blockhash)
            .SetFeePayer(signer);

        foreach (var instruction in instructions) builder.AddInstruction(instruction);

        byte[] transaction;
        try
        {
            transaction = builder.Build(signer);
        }
        catch (Exception ex)
        {
            return new SubmitResult(null, $"could not build the transaction: {ex.Message}");
        }

        var sent = await _client.SendTransactionAsync(transaction, commitment: Commitment.Confirmed);
        return sent.WasSuccessful && !string.IsNullOrWhiteSpace(sent.Result)
            ? new SubmitResult(sent.Result, null)
            : new SubmitResult(null, string.IsNullOrWhiteSpace(sent.Reason) ? sent.RawRpcResponse : sent.Reason);
    }

    /// <summary>
    /// True when the signature is confirmed without an on-chain error. A false here is never taken as
    /// "the buy did not land" on its own — the caller re-reads the token balance before any resubmit.
    /// </summary>
    public async Task<bool> ConfirmAsync(string signature, TimeSpan timeout, TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var statuses = await _client.GetSignatureStatusesAsync([signature], searchTransactionHistory: true);
            var status = statuses.WasSuccessful ? statuses.Result?.Value?.FirstOrDefault() : null;

            if (status is not null)
            {
                if (status.Error is not null) return false;

                if (status.ConfirmationStatus is "confirmed" or "finalized") return true;
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// Finds <c>bonding_curve_v2</c> for a mint by reading it off a buy the chain already accepted —
    /// see <see cref="BuyTrace"/> for why it is read rather than derived. Scans the mint's bonding-curve
    /// signatures newest-first, so the value reflects the deployment currently in force. Null when no
    /// buy carrying it exists yet.
    /// </summary>
    public async Task<PublicKey?> FindBondingCurveV2Async(PublicKey mint, int maxTransactions = 20)
    {
        var curve = PumpFunProgram.DeriveBondingCurve(mint);
        var signatures = await ListSignaturesAsync(curve, 100);

        var examined = 0;
        foreach (var signature in signatures)
        {
            if (examined++ >= maxTransactions) break;

            var found = await FindBondingCurveV2InTransactionAsync(signature);
            if (found is not null) return found;
        }

        return null;
    }

    /// <summary>
    /// ⚠️ Raw JSON-RPC on purpose, for the same class of reason as
    /// <see cref="FindBondingCurveV2InTransactionAsync"/>. Solnet 6.1.0 deserialises each entry's
    /// <c>err</c> into its own <c>TransactionError</c> model, and that model does not cover every
    /// variant the chain emits today — an <c>InsufficientFundsForRent</c> entry (whose value is an
    /// object, <c>{"account_index":0}</c>, not a string or a <c>{"Custom":n}</c> pair) fails the whole
    /// response with "Unable to parse json". One unrelated failed transaction in the curve's recent
    /// history would then block every buy on the mint, because this listing is how
    /// <c>bonding_curve_v2</c> is discovered.
    ///
    /// We only ever need the signature strings, so the errors are not decoded at all.
    /// </summary>
    private async Task<IReadOnlyList<string>> ListSignaturesAsync(PublicKey address, int limit)
    {
        var payload = $$"""
            {"jsonrpc":"2.0","id":1,"method":"getSignaturesForAddress","params":["{{address.Key}}",
             {"limit":{{limit}},"commitment":"confirmed"}]}
            """;

        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync(_rpcUrl, content);

        if (!response.IsSuccessStatusCode)
            throw new SniperException(
                $"Could not list transactions for {address.Key}: the RPC answered {(int)response.StatusCode}.");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        if (json.RootElement.TryGetProperty("error", out var error))
            throw new SniperException($"Could not list transactions for {address.Key}: {error}");

        if (!json.RootElement.TryGetProperty("result", out var result) || result.ValueKind is not JsonValueKind.Array)
            throw new SniperException($"Could not list transactions for {address.Key}: the RPC returned no result array.");

        return result.EnumerateArray()
            .Select(entry => entry.TryGetProperty("signature", out var signature) ? signature.GetString() : null)
            .Where(signature => !string.IsNullOrEmpty(signature))
            .Select(signature => signature!)
            .ToList();
    }

    /// <summary>
    /// ⚠️ Raw JSON-RPC on purpose. Solnet 6.1.0's <c>GetTransactionAsync</c> sends no
    /// <c>maxSupportedTransactionVersion</c> and the node then refuses every versioned (v0)
    /// transaction — which is most pump.fun traffic, routers included. Verified against the endpoint:
    /// "Transaction version (0) is not supported by the requesting client."
    /// </summary>
    private async Task<PublicKey?> FindBondingCurveV2InTransactionAsync(string signature)
    {
        var payload = $$"""
            {"jsonrpc":"2.0","id":1,"method":"getTransaction","params":["{{signature}}",
             {"encoding":"json","maxSupportedTransactionVersion":0,"commitment":"confirmed"}]}
            """;

        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync(_rpcUrl, content);
        if (!response.IsSuccessStatusCode) return null;

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!json.RootElement.TryGetProperty("result", out var result) || result.ValueKind is JsonValueKind.Null)
            return null;

        if (!result.TryGetProperty("meta", out var meta)) return null;

        // A failed transaction proves nothing about the accounts the program accepts.
        if (meta.TryGetProperty("err", out var error) && error.ValueKind is not JsonValueKind.Null) return null;

        var message = result.GetProperty("transaction").GetProperty("message");
        var keys = message.GetProperty("accountKeys").EnumerateArray().Select(k => k.GetString()!).ToList();

        // Versioned transactions resolve the tail of their account list from lookup tables.
        if (meta.TryGetProperty("loadedAddresses", out var loaded) && loaded.ValueKind is JsonValueKind.Object)
        {
            keys.AddRange(loaded.GetProperty("writable").EnumerateArray().Select(k => k.GetString()!));
            keys.AddRange(loaded.GetProperty("readonly").EnumerateArray().Select(k => k.GetString()!));
        }

        var raw = message.GetProperty("instructions").EnumerateArray().ToList();

        // Most live buys arrive as a CPI from a router, so the top-level instruction is not the buy.
        if (meta.TryGetProperty("innerInstructions", out var inner) && inner.ValueKind is JsonValueKind.Array)
            foreach (var group in inner.EnumerateArray())
                raw.AddRange(group.GetProperty("instructions").EnumerateArray());

        var decoder = new Base58Encoder();
        var instructions = new List<TraceInstruction>(raw.Count);

        foreach (var instruction in raw)
        {
            if (!instruction.TryGetProperty("accounts", out var accounts)) continue;
            if (!instruction.TryGetProperty("data", out var data)) continue;

            byte[] bytes;
            try
            {
                bytes = decoder.DecodeData(data.GetString() ?? "");
            }
            catch
            {
                continue;
            }

            instructions.Add(new TraceInstruction(
                instruction.GetProperty("programIdIndex").GetInt32(),
                accounts.EnumerateArray().Select(a => a.GetInt32()).ToList(),
                bytes));
        }

        return BuyTrace.FindBondingCurveV2(keys, instructions);
    }

    private async Task<byte[]?> GetAccountDataAsync(PublicKey address)
    {
        var account = await _client.GetAccountInfoAsync(address.Key, Commitment.Confirmed);
        if (!account.WasSuccessful)
            throw new SniperException($"Could not read the account {address.Key}: {account.Reason}");

        var encoded = account.Result?.Value?.Data?.FirstOrDefault();
        return string.IsNullOrEmpty(encoded) ? null : Convert.FromBase64String(encoded);
    }

    /// <summary>Mirrors monkify-backend's SolanaRpcClientProvider.MaskRpcUrl — host kept, key hidden.</summary>
    public static string MaskRpcUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "(none)";

        try
        {
            var parsed = new Uri(url);
            return string.IsNullOrEmpty(parsed.Query) ? $"{parsed.Scheme}://{parsed.Host}" : $"{parsed.Scheme}://{parsed.Host}/***";
        }
        catch
        {
            return "(unparseable rpc url)";
        }
    }
}
