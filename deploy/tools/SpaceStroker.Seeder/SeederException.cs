namespace SpaceStroker.Seeder;

/// <summary>Fatal misconfiguration or startup-guard failure. Always exits the process.</summary>
public sealed class SeederException : Exception
{
    public SeederException(string message) : base(message) { }
}
