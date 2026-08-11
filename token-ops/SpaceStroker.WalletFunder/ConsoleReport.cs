namespace SpaceStroker.WalletFunder;

public static class ConsoleReport
{
    /// <summary>Short enough to read, long enough to eyeball against the wallet you meant.</summary>
    public static string ShortAddress(string address)
        => address.Length <= 14 ? address : $"{address[..6]}…{address[^5..]}";

    public static void PrintPlan(
        FundingPlan plan, Receipts receipts, decimal availableSol, decimal feeHeadroomSol, TimeSpan window, bool live)
    {
        Console.WriteLine();
        Console.WriteLine(live ? "FUNDING PLAN (LIVE)" : "FUNDING PLAN (dry run — nothing will be sent)");
        Console.WriteLine(new string('-', 84));
        Console.WriteLine($"{"#",-4}{"LABEL",-10}{"ADDRESS",-18}{"AMOUNT (SOL)",16}  {"IDEM",-10}STATUS");

        var index = 0;
        foreach (var transfer in plan.Transfers)
        {
            index++;
            var entry = receipts.For(transfer);
            Console.WriteLine(
                $"{index,-4}{transfer.Label,-10}{ShortAddress(transfer.Address),-18}" +
                $"{WalletPool.FormatSol(transfer.SolAmount),16}  {transfer.Idem.ToString("D")[..8],-10}{entry.Status}");
        }

        Console.WriteLine(new string('-', 84));
        Console.WriteLine($"wallets        : {plan.Transfers.Count}");
        Console.WriteLine($"batch total    : {WalletPool.FormatSol(plan.TotalSol)} SOL");
        Console.WriteLine($"fee headroom   : {WalletPool.FormatSol(feeHeadroomSol)} SOL");
        Console.WriteLine($"coinbase bal.  : {WalletPool.FormatSol(availableSol)} SOL");
        Console.WriteLine($"spread window  : {(window == TimeSpan.Zero ? "none (back to back)" : $"{window.TotalMinutes:0} min")}");
        Console.WriteLine($"plan hash      : {plan.PlanHash[..16]}…");
        Console.WriteLine($"receipts       : {receipts.Path}");
        Console.WriteLine();
    }

    /// <summary>
    /// Two deliberate acts before money moves: the --execute flag AND typing the exact total.
    /// A bare flag would let a shell-history recall spend the batch.
    /// </summary>
    public static bool ConfirmTotal(decimal total, int walletCount, TextReader input)
    {
        var expected = WalletPool.FormatSol(total);

        Console.WriteLine($"About to send {expected} SOL to {walletCount} wallet(s). This CANNOT be undone.");
        Console.Write($"Type the exact total ({expected}) to proceed: ");

        var typed = (input.ReadLine() ?? "").Trim();
        return string.Equals(typed, expected, StringComparison.Ordinal);
    }

    public static void PrintSummary(Receipts receipts)
    {
        Console.WriteLine();
        Console.WriteLine("SUMMARY");
        Console.WriteLine(new string('-', 84));
        Console.WriteLine($"{"LABEL",-10}{"ADDRESS",-18}{"AMOUNT",14}  {"STATUS",-14}TRANSACTION");

        foreach (var entry in receipts.Entries)
            Console.WriteLine(
                $"{entry.Label,-10}{ShortAddress(entry.Address),-18}{WalletPool.FormatSol(entry.AmountSol),14}  " +
                $"{entry.Status,-14}{entry.CoinbaseTransactionId ?? "-"}" +
                $"{(entry.Error is null ? "" : $"  ({entry.Error})")}");

        Console.WriteLine(new string('-', 84));
        foreach (var group in receipts.Entries.GroupBy(e => e.Status).OrderBy(g => g.Key))
            Console.WriteLine($"{group.Key,-14}{group.Count()}");

        Console.WriteLine($"receipts       : {receipts.Path}");
        Console.WriteLine();
    }
}
