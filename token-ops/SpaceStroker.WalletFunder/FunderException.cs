namespace SpaceStroker.WalletFunder;

/// <summary>Fatal, user-facing error. Caught once in Program.cs and printed as "error: {message}".</summary>
public sealed class FunderException(string message) : Exception(message);
