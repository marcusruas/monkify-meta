using Shouldly;
using SpaceStroker.WalletFunder.Application;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests.Application;

public class CliOptionsTests
{
    [Fact]
    public void Run_state_defaults_into_the_Receipts_folder()
    {
        // One git-ignored folder for every tool's run state, instead of four loose files in tools/.
        var options = CliOptions.Parse([]);

        options.ReceiptsPath.Replace('\\', '/').ShouldEndWith("/Receipts/receipts.json");
        options.SecretsPath.Replace('\\', '/').ShouldEndWith("/secrets.json");
    }

    [Fact]
    public void An_explicit_receipts_path_still_wins()
    {
        var options = CliOptions.Parse(["fund", "--receipts", "/tmp/custom.json"]);

        options.ReceiptsPath.ShouldBe("/tmp/custom.json");
    }
}
