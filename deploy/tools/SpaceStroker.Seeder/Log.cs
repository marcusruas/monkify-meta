namespace SpaceStroker.Seeder;

/// <summary>
/// Console-only, timestamped logging. No file, no telemetry, no external call.
/// NEVER pass a wallet private key or a launch token to any of these.
/// </summary>
public static class Log
{
    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string message, Exception ex)
        => Write("ERROR", $"{message} :: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");

    private static void Write(string level, string message)
        => Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}");
}
