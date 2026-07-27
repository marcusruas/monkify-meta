using Shouldly;
using Xunit;

namespace SpaceStroker.Deploy.Tests;

public class TeardownDecisionsTests
{
    // A realistic `terraform state list` dump of an intact workload stack.
    private static readonly string[] IntactState =
    [
        "module.cloud_run_api.google_cloud_run_v2_service.api",
        "module.cloud_run_consumers.google_cloud_run_v2_service.consumers",
        "module.data.google_redis_instance.cache",
        TeardownDecisions.SqlInstanceAddress,
        "module.network.google_compute_network.vpc",
        "module.network.google_compute_subnetwork.subnet",
        "module.network.google_service_networking_connection.private_vpc_connection",
        "module.registry.google_artifact_registry_repository.images",
    ];

    private static readonly string[] ZeroCostLeftovers =
    [
        "module.network.google_compute_network.vpc",
        "module.network.google_compute_subnetwork.subnet",
        "module.network.google_service_networking_connection.private_vpc_connection",
    ];

    [Fact]
    public void ParseStateList_TrimsAndDropsBlankLines()
    {
        var stdout = "module.network.google_compute_network.vpc\r\n" +
                     "  module.network.google_compute_subnetwork.subnet  \n" +
                     "\n" +
                     "module.data.google_redis_instance.cache\n" +
                     "   \n";

        TeardownDecisions.ParseStateList(stdout).ShouldBe(
        [
            "module.network.google_compute_network.vpc",
            "module.network.google_compute_subnetwork.subnet",
            "module.data.google_redis_instance.cache",
        ]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\r\n")]
    public void ParseStateList_EmptyOutput_ReturnsEmpty(string stdout) =>
        TeardownDecisions.ParseStateList(stdout).ShouldBeEmpty();

    [Fact]
    public void Contains_MatchesExactAddressOnly()
    {
        TeardownDecisions.Contains(IntactState, TeardownDecisions.SqlInstanceAddress).ShouldBeTrue();

        // A longer address that merely SHARES the prefix must not count as the instance.
        string[] replicaOnly = ["module.data.google_sql_database_instance.postgres_replica"];
        TeardownDecisions.Contains(replicaOnly, TeardownDecisions.SqlInstanceAddress).ShouldBeFalse();
    }

    [Fact]
    public void Contains_SqlInstanceAbsent_IsFalse() =>
        // This is the resume case: the instance is already destroyed, so the targeted apply is skipped.
        TeardownDecisions.Contains(ZeroCostLeftovers, TeardownDecisions.SqlInstanceAddress).ShouldBeFalse();

    [Fact]
    public void IsZeroCostOnly_EmptyList_IsFalse() =>
        // An empty state is a COMPLETE teardown, not a "leftover" — the caller must not print a warning.
        TeardownDecisions.IsZeroCostOnly([]).ShouldBeFalse();

    [Fact]
    public void IsZeroCostOnly_AllNetworkAddresses_IsTrue() =>
        TeardownDecisions.IsZeroCostOnly(ZeroCostLeftovers).ShouldBeTrue();

    [Fact]
    public void IsZeroCostOnly_AnyBillableAddress_IsFalse()
    {
        string[] withSql = [.. ZeroCostLeftovers, TeardownDecisions.SqlInstanceAddress];

        TeardownDecisions.IsZeroCostOnly(withSql).ShouldBeFalse();
    }

    [Fact]
    public void IsZeroCostOnly_RejectsNearMissPrefix()
    {
        // "module.networking_extra." starts with "module.network" but is NOT the network module.
        string[] nearMiss = ["module.networking_extra.google_compute_address.egress"];

        TeardownDecisions.IsZeroCostOnly(nearMiss).ShouldBeFalse();
    }

    [Fact]
    public void DestroyAttempts_IsThreeWithSixtyAndOneTwentySecondBackoff()
    {
        TeardownDecisions.DestroyAttempts.ShouldBe(3);
        TeardownDecisions.DestroyBackoff.ShouldBe([TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120)]);
    }
}
