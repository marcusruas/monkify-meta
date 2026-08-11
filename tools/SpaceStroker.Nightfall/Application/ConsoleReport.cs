using SpaceStroker.Nightfall.Domain;
using SpaceStroker.Nightfall.Infrastructure;
using SpaceStroker.Nightfall.Infrastructure.Solana;

namespace SpaceStroker.Nightfall.Application;

/// <summary>
/// The operator's console. Never prints a private key, and never prints the RPC URL unmasked.
/// </summary>
public static class ConsoleReport
{
    public static void Banner(SecretsFile secrets, MintInfo mint, string url, string receiptsPath)
    {
        Console.WriteLine();
        Console.WriteLine("  spacestroker-nightfall");
        Console.WriteLine("  ─────────────────────────────────────────────────────────────");
        Console.WriteLine($"  dashboard : {url}");
        Console.WriteLine($"  token     : {secrets.Token.Mint}");
        Console.WriteLine($"  supply    : {TokenAmount.Format(TokenAmount.ToUi(mint.Supply, mint.Decimals))} ({mint.Decimals} decimals)");
        Console.WriteLine($"  wallets   : {secrets.Pool.Count}");
        Console.WriteLine($"  rpc       : {RpcUrlMask.HostOnly(secrets.Solana.RpcUrl)}");
        Console.WriteLine($"  jupiter   : {secrets.Jupiter.ApiBaseUrl}");
        Console.WriteLine($"  slippage  : {TokenAmount.Format(secrets.Jupiter.SlippageBps / 100m)}%  ·  " +
                          $"max impact {TokenAmount.Format(secrets.Jupiter.MaxPriceImpactPercent)}%  ·  " +
                          $"concurrency {secrets.Sell.MaxConcurrentSells}");
        Console.WriteLine($"  receipts  : {receiptsPath}");
        Console.WriteLine();
        Console.WriteLine("  MODE: SIMULATE — nothing will be broadcast until you switch to LIVE in the dashboard.");
        Console.WriteLine();
    }

    public static void Mode(bool live)
        => Console.WriteLine(live
            ? "  ⚠️  MODE: LIVE — sells will be signed and broadcast for real."
            : "  MODE: SIMULATE — nothing will be broadcast.");

    public static void Event(SellEvent e)
    {
        var detail = e.Status switch
        {
            "Sold" => $"realized {TokenAmount.Format(e.RealizedSol ?? 0m)} SOL · {e.Signature}",
            "Sent" => e.Signature ?? "",
            "Quoted" => $"quoted {TokenAmount.Format(e.QuotedSol ?? 0m)} SOL",
            _ => e.Message ?? "",
        };

        Console.WriteLine($"  [{e.Status,-11}] {e.Alias,-16} {detail}");
    }

    public static void Log(string message) => Console.WriteLine($"  {message}");
}
