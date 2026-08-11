namespace SpaceStroker.TokenSniper.Domain;

/// <summary>Anything that should stop the run with an actionable message and exit code 1.</summary>
public class SniperException : Exception
{
    public SniperException(string message) : base(message) { }
}

/// <summary>A bad command line — exit code 2, matching spacestroker-fund-wallets.</summary>
public sealed class UsageException : SniperException
{
    public UsageException(string message) : base(message) { }
}
