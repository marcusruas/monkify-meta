using System.Globalization;
using System.Net;
using System.Text;
using SpaceStroker.TokenSniper.Domain;

namespace SpaceStroker.TokenSniper.Infrastructure.Reports;

/// <summary>
/// The human-readable run summary: one self-contained page, no external CSS, JS, fonts or images —
/// it has to open from disk on a machine with no network. Solscan links are anchors, not assets.
///
/// ⚠️ Nothing here may render a private key or the keyed RPC URL: it is fed a <see cref="RunReport"/>,
/// which does not carry either.
/// </summary>
public static class HtmlReportWriter
{
    public const string FileName = "snipe-report.html";

    public static string Write(RunReport report, string directory)
    {
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, FileName);
        File.WriteAllText(path, Render(report), new UTF8Encoding(false));
        return path;
    }

    public static string Render(RunReport report)
    {
        var html = new StringBuilder();
        var decimals = report.TokenDecimals;

        html.Append("""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Ghost wallet snipe report</title>
            <style>
              :root { color-scheme: light dark; --line:#8883; }
              body { font: 14px/1.5 ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; margin: 2rem auto; max-width: 72rem; padding: 0 1rem; }
              h1 { font-size: 1.3rem; margin-bottom: .25rem; }
              .sub { opacity: .7; margin-top: 0; }
              .totals { display: grid; grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr)); gap: .75rem; margin: 1.5rem 0; }
              .card { border: 1px solid var(--line); border-radius: 4px; padding: .75rem 1rem; }
              .card b { display: block; font-size: 1.25rem; }
              .card span { opacity: .7; font-size: .8rem; text-transform: uppercase; letter-spacing: .04em; }
              .scroll { overflow-x: auto; }
              table { border-collapse: collapse; width: 100%; font-size: 13px; }
              th, td { border-bottom: 1px solid var(--line); padding: .4rem .6rem; text-align: right; white-space: nowrap; }
              th:first-child, td:first-child, th:nth-child(2), td:nth-child(2) { text-align: left; }
              tbody tr:hover { background: #8881; }
              .Filled { color: #1a7f37; } .Short { color: #9a6700; } .Failed, .Unconfirmed { color: #cf222e; } .Skipped { opacity: .6; }
              .warn { border-left: 3px solid #cf222e; padding: .5rem .75rem; margin: 1rem 0; background: #cf222e11; }
              footer { margin-top: 2rem; opacity: .6; font-size: .8rem; }
            </style></head><body>
            """);

        html.Append(CultureInfo.InvariantCulture, $"<h1>Ghost wallet snipe — {Enc(report.Mint)}</h1>");
        html.Append(CultureInfo.InvariantCulture,
            $"<p class=\"sub\">{(report.DryRun ? "DRY RUN — nothing was submitted" : "Executed")} · " +
            $"started {report.StartedUtc:u} · finished {(report.FinishedUtc?.ToString("u") ?? "—")}</p>");

        if (!string.IsNullOrWhiteSpace(report.StoppedReason))
            html.Append(CultureInfo.InvariantCulture, $"<div class=\"warn\"><b>Run stopped early:</b> {Enc(report.StoppedReason)}</div>");

        html.Append("<div class=\"totals\">");
        Card(html, "Pool share of supply", $"{report.PoolPercentOfSupply.ToString("0.####", CultureInfo.InvariantCulture)}%");
        Card(html, "Total SOL spent", SnipePool.FormatSol(report.TotalSolSpentLamports));
        Card(html, "Tokens acquired", SnipePool.FormatTokens(report.TotalTokensReceivedBaseUnits, decimals));
        Card(html, "Blended price (lamports/token)", report.BlendedAveragePriceLamportsPerToken.ToString("0.########", CultureInfo.InvariantCulture));
        Card(html, "Wallets filled", $"{report.Wallets.Count(w => w.Status == WalletStatus.Filled)} / {report.Wallets.Count}");
        html.Append("</div>");

        html.Append("""
            <div class="scroll"><table><thead><tr>
            <th>Alias</th><th>Wallet</th><th>Target %</th><th>Achieved %</th><th>Held before</th>
            <th>Bought</th><th>Held after</th><th>SOL spent</th><th>Avg price</th><th>Offset</th>
            <th>Status</th><th>Signature</th><th>Note</th></tr></thead><tbody>
            """);

        foreach (var wallet in report.Wallets)
        {
            html.Append("<tr>");
            html.Append(CultureInfo.InvariantCulture, $"<td>{Enc(wallet.Alias)}</td>");
            html.Append(CultureInfo.InvariantCulture,
                $"<td><a href=\"https://solscan.io/account/{Enc(wallet.PublicKey)}\">{Enc(wallet.PublicKey)}</a></td>");
            html.Append(CultureInfo.InvariantCulture, $"<td>{SnipePool.FormatPercent(wallet.TargetPercent)}</td>");
            html.Append(CultureInfo.InvariantCulture, $"<td>{SnipePool.FormatPercent(wallet.AchievedPercent)}</td>");
            html.Append(CultureInfo.InvariantCulture, $"<td>{SnipePool.FormatTokens(wallet.HeldBeforeBaseUnits, decimals)}</td>");
            html.Append(CultureInfo.InvariantCulture, $"<td>{SnipePool.FormatTokens(wallet.TokensReceivedBaseUnits, decimals)}</td>");
            html.Append(CultureInfo.InvariantCulture, $"<td>{SnipePool.FormatTokens(wallet.HeldAfterBaseUnits, decimals)}</td>");
            html.Append(CultureInfo.InvariantCulture, $"<td>{SnipePool.FormatSol(wallet.SolSpentLamports)}</td>");
            html.Append(CultureInfo.InvariantCulture,
                $"<td>{wallet.AveragePriceLamportsPerToken.ToString("0.########", CultureInfo.InvariantCulture)}</td>");
            html.Append(CultureInfo.InvariantCulture,
                $"<td>{TimeSpan.FromSeconds(wallet.ScheduledOffsetSeconds):hh\\:mm\\:ss}</td>");
            html.Append(CultureInfo.InvariantCulture, $"<td class=\"{wallet.Status}\">{wallet.Status}</td>");
            html.Append(wallet.Signature is null
                ? "<td>—</td>"
                : $"<td><a href=\"https://solscan.io/tx/{Enc(wallet.Signature)}\">{Enc(wallet.Signature[..Math.Min(12, wallet.Signature.Length)])}…</a></td>");
            html.Append(CultureInfo.InvariantCulture, $"<td>{Enc(wallet.Note ?? "")}</td>");
            html.Append("</tr>");
        }

        html.Append("</tbody></table></div>");

        var shortfalls = report.Wallets.Where(w => w.Status is WalletStatus.Short or WalletStatus.Failed or WalletStatus.Unconfirmed).ToList();
        if (shortfalls.Count > 0)
        {
            html.Append("<h2>Wallets that did not reach target</h2><ul>");
            foreach (var wallet in shortfalls)
                html.Append(CultureInfo.InvariantCulture,
                    $"<li><b>{Enc(wallet.Alias)}</b> — {wallet.Status}: {Enc(wallet.Note ?? "no reason recorded")}</li>");
            html.Append("</ul>");
        }

        html.Append(CultureInfo.InvariantCulture,
            $"<footer>Total supply {SnipePool.FormatTokens(report.MintTotalSupplyBaseUnits, decimals)} · generated by spacestroker-snipe-token. Contains public addresses only.</footer></body></html>");

        return html.ToString();
    }

    private static void Card(StringBuilder html, string label, string value) =>
        html.Append(CultureInfo.InvariantCulture, $"<div class=\"card\"><span>{Enc(label)}</span><b>{Enc(value)}</b></div>");

    private static string Enc(string value) => WebUtility.HtmlEncode(value);
}
