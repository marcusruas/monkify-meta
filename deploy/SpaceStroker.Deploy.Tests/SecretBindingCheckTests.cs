using System;
using System.IO;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace SpaceStroker.Deploy.Tests;

/// <summary>
/// A `secrets` entry with no Terraform binding is the quietest failure this tool can produce: the
/// value syncs to Secret Manager, the apply succeeds, "Deploy complete." prints, and the container
/// never receives the variable — so the app falls back to its committed default and a fail-closed
/// feature just does nothing. It shipped that way twice (Orbit #123, house seed #168).
///
/// <see cref="TheCommittedStack_BindsEverySecretInTheExample"/> is the one that matters: it reads the
/// REAL main.tf and the REAL secrets.example.json, so adding a key to the example without wiring it
/// up fails here rather than in production.
/// </summary>
public class SecretBindingCheckTests
{
    private const string HouseSeedKey = "GeneralSettings:Operator:Token:HouseSeed:PrivateKey";
    private const string HouseSeedId = "GeneralSettings-Operator-Token-HouseSeed-PrivateKey";

    /// <summary>Both lists carry the same two ids — the fully-wired baseline.</summary>
    private const string TfBound = """
        locals {
          base_secret_ids = [
            "ConnectionStrings-Redis",
            "GeneralSettings-Operator-Token-Orbit-PrivateKey",
          ]

          secret_ids = toset([for id in local.base_secret_ids : "${local.name_prefix}-${id}"])

          base_secret_env = {
            "ConnectionStrings__Redis"                            = "ConnectionStrings-Redis"
            "GeneralSettings__Operator__Token__Orbit__PrivateKey" = "GeneralSettings-Operator-Token-Orbit-PrivateKey"
          }
        }
        """;

    /// <summary>Mounted but never granted — the revision fails at start-up, loudly.</summary>
    private const string TfEnvOnly = """
        locals {
          base_secret_ids = [
            "ConnectionStrings-Redis",
          ]

          base_secret_env = {
            "ConnectionStrings__Redis"                               = "ConnectionStrings-Redis"
            "GeneralSettings__Operator__Token__HouseSeed__PrivateKey" = "GeneralSettings-Operator-Token-HouseSeed-PrivateKey"
          }
        }
        """;

    /// <summary>Granted but never mounted — silent, and the shape that reached production.</summary>
    private const string TfIdsOnly = """
        locals {
          base_secret_ids = [
            "ConnectionStrings-Redis",
            "GeneralSettings-Operator-Token-HouseSeed-PrivateKey",
          ]

          base_secret_env = {
            "ConnectionStrings__Redis" = "ConnectionStrings-Redis"
          }
        }
        """;

    [Fact]
    public void ColonsBecomeDashes_MatchingTheSecretManagerId()
        => SecretBindingCheck.ToSecretId(HouseSeedKey).ShouldBe(HouseSeedId);

    [Fact]
    public void FullyBoundSecrets_ReportNothing()
    {
        var keys = new[] { "ConnectionStrings:Redis", "GeneralSettings:Operator:Token:Orbit:PrivateKey" };

        SecretBindingCheck.FindUnbound(keys, TfBound).ShouldBeEmpty();
    }

    /// <summary>The #168 regression, reproduced exactly: in the secrets file, in neither list.</summary>
    [Fact]
    public void ASecretInNeitherList_IsReportedAgainstBoth()
    {
        var only = SecretBindingCheck.FindUnbound(new[] { HouseSeedKey }, TfBound).ShouldHaveSingleItem();

        only.ShouldContain(HouseSeedId);
        only.ShouldContain("base_secret_ids AND base_secret_env");
    }

    [Fact]
    public void MountedWithoutIam_SaysTheRevisionWillNotStart()
        => SecretBindingCheck.FindUnbound(new[] { HouseSeedKey }, TfEnvOnly)
            .ShouldHaveSingleItem()
            .ShouldContain("fail to start");

    [Fact]
    public void GrantedButNeverMounted_SaysExactlyThat()
        => SecretBindingCheck.FindUnbound(new[] { HouseSeedKey }, TfIdsOnly)
            .ShouldHaveSingleItem()
            .ShouldContain("never mounted");

    [Fact]
    public void ARenamedBlock_FailsLoudlyRatherThanPassingVacuously()
        => Should.Throw<DeployException>(
                () => SecretBindingCheck.FindUnbound(new[] { "ConnectionStrings:Redis" }, "locals {}"))
            .Message.ShouldContain("base_secret_ids");

    /// <summary>
    /// The real guard. secrets.example.json is the template every secrets.&lt;env&gt;.json is copied
    /// from, so a key that exists there must be wired into the committed stack.
    /// </summary>
    [Fact]
    public void TheCommittedStack_BindsEverySecretInTheExample()
    {
        var repo = RepoRoot();
        var mainTf = File.ReadAllText(Path.Combine(repo, "deploy", "terraform", "workload", "main.tf"));
        var example = File.ReadAllText(Path.Combine(repo, "deploy", "secrets.example.json"));

        var keys = JsonSerializer.Deserialize<SecretsFile>(example, SecretsFile.JsonOpts)!.Secrets.Keys;

        keys.ShouldNotBeEmpty();
        SecretBindingCheck.FindUnbound(keys, mainTf).ShouldBeEmpty();
    }

    [Fact]
    public void TheHouseSeedKey_IsBound()
    {
        var mainTf = File.ReadAllText(
            Path.Combine(RepoRoot(), "deploy", "terraform", "workload", "main.tf"));

        SecretBindingCheck.FindUnbound(new[] { HouseSeedKey }, mainTf).ShouldBeEmpty();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "deploy", "terraform", "workload", "main.tf")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the monkify-meta repo root from the test binary.");
    }
}
