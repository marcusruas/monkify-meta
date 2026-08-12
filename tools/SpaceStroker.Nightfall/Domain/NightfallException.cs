namespace SpaceStroker.Nightfall.Domain;

/// <summary>
/// Anything that stops a run or a single wallet's sell. Mapped to exit code 1 by Program.cs.
/// </summary>
/// <remarks>
/// ⚠️ The message reaches the operator's console AND the browser. Never put a private key, a raw
/// secrets value, or an unmasked RPC URL in one.
/// </remarks>
public class NightfallException(string message) : Exception(message);

/// <summary>A bad command line. Mapped to exit code 2, matching spacestroker-fund-wallets.</summary>
public sealed class UsageException(string message) : NightfallException(message);
