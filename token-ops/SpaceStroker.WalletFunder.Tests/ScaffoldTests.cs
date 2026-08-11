using Shouldly;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests;

public class ScaffoldTests
{
    [Fact]
    public void FunderException_carries_its_message()
        => new FunderException("boom").Message.ShouldBe("boom");
}
