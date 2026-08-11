using System.Linq;
using Shouldly;
using Xunit;

namespace SpaceStroker.Deploy.Tests;

/// <summary>
/// `spacestroker-deploy validate` prints exactly what <see cref="EnvNaming.DerivedIds"/> returns, so
/// an id missing from that list is an id nobody checks against its GCP cap until a half-applied
/// stack fails in the middle of an apply.
///
/// These also pin the monitoring names themselves: Terraform hard-codes the same strings, and
/// `validate` printing one name while Terraform creates another is a silent lie.
/// </summary>
public class EnvNamingTests
{
    private static EnvNaming For(string environment) => EnvNaming.From(new SecretsFile
    {
        Environment = environment,
        Gcp = new GcpSection { ProjectId = "spacestroker-live", Region = "us-central1" },
    });

    private static EnvNaming Production() => For("production");

    [Fact]
    public void MonitoringIds_AreEnvScoped()
    {
        var n = Production();

        n.DashboardName.ShouldBe("spacestroker-prod-overview");
        n.UptimeCheckApi.ShouldBe("spacestroker-prod-uptime-api");
        n.UptimeCheckApp.ShouldBe("spacestroker-prod-uptime-app");
        n.NotificationChannel.ShouldBe("spacestroker-prod-ops-email");
        n.ArmorDeniedMetric.ShouldBe("spacestroker-prod-armor-denied");
        n.ArmorPreviewMetric.ShouldBe("spacestroker-prod-armor-preview");
    }

    [Fact]
    public void MonitoringIds_CarryTheDevelopmentCode_WhenTheEnvironmentIsDevelopment()
    {
        // Development creates none of these today (enable_dashboards is false), but the names must
        // stay env-scoped: dev and prod share ONE GCP project, so an unscoped id would collide the
        // day someone turns dashboards on for development.
        For("development").DashboardName.ShouldBe("spacestroker-dev-overview");
    }

    [Fact]
    public void DerivedIds_IncludesEveryMonitoringId_SoValidateActuallyChecksThem()
    {
        var ids = Production().DerivedIds().Select(i => i.Id).ToArray();

        ids.ShouldContain("spacestroker-prod-overview");
        ids.ShouldContain("spacestroker-prod-uptime-api");
        ids.ShouldContain("spacestroker-prod-uptime-app");
        ids.ShouldContain("spacestroker-prod-ops-email");
        ids.ShouldContain("spacestroker-prod-armor-denied");
        ids.ShouldContain("spacestroker-prod-armor-preview");
    }

    [Fact]
    public void DerivedIds_MarksEveryMonitoringIdAsFittingItsCap()
        => Production().DerivedIds()
            .Where(i => i.Id.Contains("overview") || i.Id.Contains("uptime")
                     || i.Id.Contains("ops-email") || i.Id.Contains("armor"))
            .ShouldAllBe(i => i.Ok);

    [Fact]
    public void Validate_AcceptsTheMonitoringIds()
        => Should.NotThrow(() => Production().Validate());
}
