namespace SpaceStroker.WalletFunder;

/// <summary>Fatal, user-facing error. Caught once in Program.cs and printed as "error: {message}".</summary>
public sealed class FunderException(string message) : Exception(message);

/// <summary>Bad command line. Exits 2 so a scripted caller can tell "you typed it wrong" from "it failed".</summary>
public sealed class UsageException(string message) : Exception(message);
