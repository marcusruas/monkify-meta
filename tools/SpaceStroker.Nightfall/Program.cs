using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Solnet.Rpc;
using SpaceStroker.Nightfall.Application;
using SpaceStroker.Nightfall.Domain;
using SpaceStroker.Nightfall.Infrastructure;
using SpaceStroker.Nightfall.Infrastructure.Jupiter;
using SpaceStroker.Nightfall.Infrastructure.Solana;

// spacestroker-nightfall — a localhost dashboard over a Solana wallet pool: see what each wallet
// holds and sell it, one or many concurrently.
//
// ⚠️ This tool sells real holdings and a sale cannot be undone. The guardrails (SIMULATE on at
// startup, the confirmation dialog, the price-impact abort, the low-SOL exclusion, click-never-sells)
// are the feature. Do not simplify them away.

try
{
    var options = CliOptions.Parse(args);

    if (options.ShowHelp)
    {
        CliOptions.PrintUsage();
        return 0;
    }

    var secrets = SecretsFile.Load(options.SecretsPath);

    if (secrets.Pool.Count == 0)
    {
        // A clean no-op, exactly like the funder's empty pool: nothing is attempted and no external
        // service is reached.
        Console.WriteLine($"The wallet pool in {options.SecretsPath} is empty — nothing to show. Add wallets and re-run.");
        return 0;
    }

    var rpc = ClientFactory.GetClient(secrets.Solana.RpcUrl);
    var reader = new SolanaReader(rpc, secrets.Nightfall.Token.Mint);

    // Read the mint once. Every percentage on screen depends on it, so a failure here is fatal
    // rather than a dashboard full of zeroes.
    var mint = await reader.GetMintAsync(CancellationToken.None);

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    var jupiter = new JupiterClient(http, secrets.Nightfall.Jupiter);
    var quotes = new QuoteCache(TimeSpan.FromSeconds(Math.Max(1, secrets.Nightfall.Jupiter.QuoteCacheSeconds)));

    var state = new NightfallState(secrets.Pool, new SecretsView(
        secrets.Nightfall.Token.Mint,
        secrets.Nightfall.Sell.MinSolForFeeSol,
        secrets.Nightfall.Jupiter.SlippageBps,
        secrets.Nightfall.Jupiter.MaxPriceImpactPercent,
        secrets.Nightfall.Sell.MaxConcurrentSells))
    {
        Mint = mint,
    };

    var receipts = ReceiptsStore.LoadOrCreate(options.ReceiptsPath, secrets.Nightfall.Token.Mint);
    var events = new EventStream();
    var poll = new PollLoop(state, secrets, reader, jupiter, ConsoleReport.Log);

    var builder = WebApplication.CreateBuilder();

    // 127.0.0.1 ONLY. There is no authentication, and none is needed *because* of this bind —
    // widening it turns the tool into a remote wallet-drainer.
    builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");
    builder.Logging.ClearProviders();

    var app = builder.Build();
    app.UseDefaultFiles();
    app.UseStaticFiles();

    var lifetime = app.Lifetime.ApplicationStopping;

    // The runner takes the APPLICATION's token, never a request's — see SellRunner.RunAsync.
    var runner = new SellRunner(
        state, secrets, jupiter, reader,
        new SolanaSender(rpc),
        new TokenAccountCloser(rpc, reader),
        receipts,
        sellEvent =>
        {
            ConsoleReport.Event(sellEvent);
            events.Publish("sell", sellEvent);
        },
        lifetime);

    ApiEndpoints.Map(app, state, secrets, jupiter, quotes, runner, poll, events);

    var url = $"http://127.0.0.1:{options.Port}";
    ConsoleReport.Banner(secrets, mint, url, options.ReceiptsPath);

    await poll.RefreshAsync(CancellationToken.None);

    _ = Task.Run(() => poll.RunAsync(lifetime), lifetime);

    if (options.OpenBrowser)
        TryOpenBrowser(url);

    await app.RunAsync();

    return 0;
}
catch (UsageException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    CliOptions.PrintUsage();
    return 2;
}
catch (NightfallException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static void TryOpenBrowser(string url)
{
    try
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    catch (Exception)
    {
        // Headless or no default browser — the URL is already printed in the banner.
    }
}
