using Shouldly;
using SpaceStroker.Nightfall.Application;
using Xunit;

namespace SpaceStroker.Nightfall.Tests.Application;

public class CliOptionsTests
{
    [Fact]
    public void Config_defaults_to_the_shared_secrets_file_and_the_Receipts_folder()
    {
        var options = CliOptions.Parse([]);

        options.SecretsPath.Replace('\\', '/').ShouldEndWith("/secrets.json");
        options.SecretsPath.ShouldNotContain("nightfall.json");
        options.ReceiptsPath.Replace('\\', '/').ShouldEndWith("/Receipts/nightfall-receipts.json");
    }

    [Fact]
    public void Explicit_paths_still_win()
    {
        var options = CliOptions.Parse(["--secrets", "/tmp/s.json", "--receipts", "/tmp/r.json"]);

        options.SecretsPath.ShouldBe("/tmp/s.json");
        options.ReceiptsPath.ShouldBe("/tmp/r.json");
    }
}
