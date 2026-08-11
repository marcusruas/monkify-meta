using Shouldly;
using SpaceStroker.TokenSniper.Application;
using SpaceStroker.TokenSniper.Domain;
using Xunit;

namespace SpaceStroker.TokenSniper.Tests.Application;

public class CliOptionsTests
{
    [Fact]
    public void Parse_DefaultsToADryRunOfTheSnipeCommand()
    {
        var options = CliOptions.Parse([]);

        options.Command.ShouldBe("snipe");
        options.Execute.ShouldBeFalse();
        options.SecretsPath.ShouldEndWith("secrets.json");
        options.ReceiptsPath.ShouldEndWith("snipe-receipts.json");
        options.ReportsRootPath.ShouldEndWith("reports");
    }

    [Fact]
    public void Parse_ReadsEveryOption()
    {
        var options = CliOptions.Parse([
            "snipe", "--secrets", "s.json", "--pool", "p.json", "--receipts", "r.json",
            "--reports", "out", "--execute", "--spread-minutes", "15",
        ]);

        options.SecretsPath.ShouldBe("s.json");
        options.PoolPath.ShouldBe("p.json");
        options.ReceiptsPath.ShouldBe("r.json");
        options.ReportsRootPath.ShouldBe("out");
        options.Execute.ShouldBeTrue();
        options.SpreadMinutes.ShouldBe(15);
    }

    [Fact]
    public void Parse_AccumulatesRepeatedWalletFilters()
    {
        var options = CliOptions.Parse(["snipe", "--wallet", "W01", "--wallet", "W07"]);

        options.OnlyAliases.ShouldBe(["W01", "W07"]);
    }

    [Fact]
    public void Parse_RefusesExecuteOnTheReadOnlyStatusCommand()
    {
        // `status` must be structurally incapable of spending — that is the whole point of having it.
        Should.Throw<UsageException>(() => CliOptions.Parse(["status", "--execute"]));
    }

    [Fact]
    public void Parse_RejectsAnUnknownOption()
    {
        Should.Throw<UsageException>(() => CliOptions.Parse(["snipe", "--yolo"]))
            .Message.ShouldContain("--yolo");
    }

    [Fact]
    public void Parse_RejectsAnOptionMissingItsValue()
    {
        Should.Throw<UsageException>(() => CliOptions.Parse(["snipe", "--secrets"]))
            .Message.ShouldContain("needs a value");
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("-5")]
    public void Parse_RejectsABadSpreadWindow(string value)
    {
        Should.Throw<UsageException>(() => CliOptions.Parse(["snipe", "--spread-minutes", value]));
    }

    [Fact]
    public void Parse_AcceptsAZeroSpreadForBackToBackBuys()
    {
        CliOptions.Parse(["snipe", "--spread-minutes", "0"]).SpreadMinutes.ShouldBe(0);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("help")]
    public void Parse_ShowsHelp(string flag)
    {
        CliOptions.Parse([flag]).ShowHelp.ShouldBeTrue();
    }
}
