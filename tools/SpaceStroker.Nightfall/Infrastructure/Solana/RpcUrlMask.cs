namespace SpaceStroker.Nightfall.Infrastructure.Solana;

/// <summary>
/// The keyed RPC endpoint carries its API key in the query string, so it can never be logged whole.
/// Same rule specs/solana-rpc-api-key.md established for the backend (MaskRpcUrl).
/// </summary>
public static class RpcUrlMask
{
    public static string HostOnly(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "(unset)";

        return Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            ? $"{parsed.Scheme}://{parsed.Host}"
            : "(unparseable endpoint)";
    }
}
