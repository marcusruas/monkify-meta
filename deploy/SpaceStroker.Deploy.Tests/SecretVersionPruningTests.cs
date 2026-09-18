using System;
using System.Linq;
using Shouldly;
using Xunit;

namespace SpaceStroker.Deploy.Tests;

/// <summary>
/// The arithmetic behind this class is the whole reason it exists: Secret Manager bills the
/// "Secret version replica storage" SKU per ENABLED VERSION per month, not per secret. The tool used
/// to push a new version for every key on every deploy and destroy nothing, so 24 secrets had grown
/// to 692 live versions — R$118.77 in the first 17 days of 2026-09 for values nothing ever read
/// (Cloud Run mounts `version = "latest"`).
///
/// Every test here guards a way this could destroy the wrong version, which is unrecoverable: the
/// payload of a destroyed version is gone for good. The ordering tests matter most — gcloud prints
/// version names as strings, and sorting "10" before "9" would destroy the LIVE value.
/// </summary>
public class SecretVersionPruningTests
{
    /// <summary>The decision from the cost review: the live value plus one rollback step.</summary>
    [Fact]
    public void Retention_IsTheLiveVersionPlusOne()
    {
        SecretVersionPruning.VersionsToKeep.ShouldBe(2);
    }

    // --- Parsing gcloud's output ------------------------------------------------

    /// <summary>
    /// `--format="csv[no-heading](name,state)"`. Files and pipes on Windows come back CRLF-terminated
    /// and a stray \r would make int.Parse fail on every line, so it is stripped here rather than at
    /// each call site.
    /// </summary>
    [Fact]
    public void Parse_ReadsNumberAndState_ToleratingCrlfAndBlankLines()
    {
        var versions = SecretVersionPruning.Parse("53,enabled\r\n52,enabled\r\n\r\n51,destroyed\r\n");

        versions.Select(v => v.Number).ShouldBe(new[] { 53, 52, 51 });
        versions.Select(v => v.State).ShouldBe(new[] { "enabled", "enabled", "destroyed" });
    }

    [Fact]
    public void Parse_IsCaseInsensitiveAboutState()
    {
        var versions = SecretVersionPruning.Parse("7,ENABLED\n6,Destroyed");

        versions[0].IsEnabled.ShouldBeTrue();
        versions[1].IsDestroyed.ShouldBeTrue();
    }

    [Fact]
    public void Parse_ReturnsNothingForEmptyOutput()
    {
        SecretVersionPruning.Parse("").ShouldBeEmpty();
        SecretVersionPruning.Parse("\r\n").ShouldBeEmpty();
    }

    /// <summary>
    /// A line this parser does not understand means gcloud changed its output shape. Guessing would
    /// mean miscounting the retention window and destroying a version that should have been kept, so
    /// it fails loudly instead.
    /// </summary>
    [Fact]
    public void Parse_ThrowsWhenAVersionNameIsNotANumber()
    {
        Should.Throw<DeployException>(() => SecretVersionPruning.Parse("latest,enabled"));
        Should.Throw<DeployException>(() => SecretVersionPruning.Parse("53"));
    }

    // --- Choosing what to destroy -----------------------------------------------

    [Fact]
    public void ToDestroy_KeepsTheNewestEnabledVersionsAndReturnsTheRest()
    {
        var versions = SecretVersionPruning.Parse("5,enabled\n4,enabled\n3,enabled\n2,enabled\n1,enabled");

        SecretVersionPruning.ToDestroy(versions, keep: 2).ShouldBe(new[] { 1, 2, 3 });
    }

    /// <summary>
    /// The one that would cost real money to get wrong. Version names are strings in gcloud's output:
    /// sorted as text, "9" is greater than "10", so a lexical sort would keep versions 9 and 8 and
    /// destroy 10 — the version every running container resolves `latest` to.
    /// </summary>
    [Fact]
    public void ToDestroy_OrdersNumerically_NotLexically()
    {
        var versions = SecretVersionPruning.Parse("10,enabled\n9,enabled\n8,enabled\n7,enabled");

        SecretVersionPruning.ToDestroy(versions, keep: 2).ShouldBe(new[] { 7, 8 });
    }

    /// <summary>
    /// Oldest first, so an interruption (Ctrl-C, expired credentials, a 500 from the API) always
    /// leaves the newest versions standing — the run stops short of the retention window rather than
    /// in the middle of it.
    /// </summary>
    [Fact]
    public void ToDestroy_ReturnsOldestFirst()
    {
        var versions = SecretVersionPruning.Parse("4,enabled\n1,enabled\n3,enabled\n2,enabled");

        SecretVersionPruning.ToDestroy(versions, keep: 1).ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public void ToDestroy_ReturnsNothingWhenTheSecretIsAtOrBelowRetention()
    {
        SecretVersionPruning.ToDestroy(SecretVersionPruning.Parse("2,enabled\n1,enabled"), keep: 2).ShouldBeEmpty();
        SecretVersionPruning.ToDestroy(SecretVersionPruning.Parse("1,enabled"), keep: 2).ShouldBeEmpty();
        SecretVersionPruning.ToDestroy(SecretVersionPruning.Parse(""), keep: 2).ShouldBeEmpty();
    }

    /// <summary>Destroying an already-destroyed version is an error from gcloud, and it bills nothing.</summary>
    [Fact]
    public void ToDestroy_SkipsVersionsThatAreAlreadyDestroyed()
    {
        var versions = SecretVersionPruning.Parse("5,enabled\n4,enabled\n3,destroyed\n2,destroyed\n1,enabled");

        SecretVersionPruning.ToDestroy(versions, keep: 2).ShouldBe(new[] { 1 });
    }

    /// <summary>
    /// A disabled version still bills — only DESTROYED stops the meter — and `latest` skips it, so it
    /// can never be the value Cloud Run mounts. It is therefore never worth a retention slot, even
    /// when it is the highest-numbered version in the secret.
    /// </summary>
    [Fact]
    public void ToDestroy_NeverSpendsARetentionSlotOnADisabledVersion()
    {
        var versions = SecretVersionPruning.Parse("9,disabled\n8,enabled\n7,enabled\n6,enabled");

        SecretVersionPruning.ToDestroy(versions, keep: 2).ShouldBe(new[] { 6, 9 });
    }

    /// <summary>
    /// keep: 0 would destroy the value the running services resolve `latest` to. There is no caller
    /// that should ever want that, so it is rejected rather than obeyed.
    /// </summary>
    [Fact]
    public void ToDestroy_RefusesToEmptyTheSecret()
    {
        var versions = SecretVersionPruning.Parse("2,enabled\n1,enabled");

        Should.Throw<DeployException>(() => SecretVersionPruning.ToDestroy(versions, keep: 0));
        Should.Throw<DeployException>(() => SecretVersionPruning.ToDestroy(versions, keep: -1));
    }

    // --- Deciding whether a new version is needed at all -------------------------

    /// <summary>
    /// The root cause of the 692: every deploy re-pushed byte-identical values. The stored side is the
    /// raw payload gcloud writes with `--out-file`, compared byte for byte against the same UTF-8
    /// encoding `versions add --data-file` uploads — so the round trip is symmetric and no console
    /// encoding or line-ending rewrite can sit between the two.
    /// </summary>
    [Fact]
    public void IsAlreadyStored_IsTrueOnlyWhenTheStoredPayloadMatchesExactly()
    {
        const string value = "Host=10.0.0.3;Password=s3cret";
        var stored = System.Text.Encoding.UTF8.GetBytes(value);

        SecretVersionPruning.IsAlreadyStored(value, stored).ShouldBeTrue();
        SecretVersionPruning.IsAlreadyStored(value + " ", stored).ShouldBeFalse();   // trailing space matters
        SecretVersionPruning.IsAlreadyStored(value, Array.Empty<byte>()).ShouldBeFalse(); // nothing stored yet
        SecretVersionPruning.IsAlreadyStored(value, null).ShouldBeFalse();           // read failed
    }

    /// <summary>
    /// A PEM's line endings are part of the key: rewriting CRLF to LF breaks the parser at the other
    /// end. The comparison must treat the two as different values so the corrected one gets pushed.
    /// </summary>
    [Fact]
    public void IsAlreadyStored_TreatsLineEndingChangesInsideTheValueAsAChange()
    {
        const string lf = "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----";
        var crlf = lf.Replace("\n", "\r\n");
        var storedLf = System.Text.Encoding.UTF8.GetBytes(lf);

        SecretVersionPruning.IsAlreadyStored(crlf, storedLf).ShouldBeFalse();
        SecretVersionPruning.IsAlreadyStored(lf, storedLf).ShouldBeTrue();
    }

    /// <summary>
    /// Non-ASCII in a value (a passphrase, a display name) must not read as "changed" on every deploy
    /// just because one side went through a different encoding.
    /// </summary>
    [Fact]
    public void IsAlreadyStored_RoundTripsNonAsciiValues()
    {
        const string value = "senha-com-acento-çãé";

        SecretVersionPruning.IsAlreadyStored(value, System.Text.Encoding.UTF8.GetBytes(value)).ShouldBeTrue();
    }
}
