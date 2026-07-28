using Shouldly;
using Xunit;

namespace SpaceStroker.Seeder.Tests;

public class SmokeTests
{
    [Fact]
    public void SeederException_carries_its_message()
    {
        var ex = new SeederException("boom");
        ex.Message.ShouldBe("boom");
    }
}
