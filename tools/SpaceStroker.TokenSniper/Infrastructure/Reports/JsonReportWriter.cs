using System.Text.Json;
using System.Text.Json.Serialization;
using SpaceStroker.TokenSniper.Domain;

namespace SpaceStroker.TokenSniper.Infrastructure.Reports;

/// <summary>
/// The machine-readable run summary. Deliberately independent of <see cref="HtmlReportWriter"/> —
/// the card chose two independent writers over rendering one from the other, accepting the drift risk.
/// </summary>
public static class JsonReportWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public const string FileName = "snipe-report.json";

    public static string Write(RunReport report, string directory)
    {
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, FileName);
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOpts));
        return path;
    }
}
