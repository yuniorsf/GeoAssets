using FluentAssertions;
using GeoAssets.Core.Isp.Compliance;
using GeoAssets.Core.Isp.Compliance.PoleAudit;
using Xunit;

namespace GeoAssets.Core.Tests.Isp.Compliance.PoleAudit;

/// <summary>Covers <see cref="InMemoryPoleAuditEvidenceRepository"/>'s add/build-bundle behavior, independent of the HTTP façade.</summary>
public class InMemoryPoleAuditEvidenceRepositoryTests
{
    private static readonly DateTimeOffset CutoffUtc = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static PoleAuditEvidenceEntry Evidence(string poleFeatureId, DateTimeOffset capturedAtUtc, PoleAuditEvidenceKind kind = PoleAuditEvidenceKind.AnchorPhoto) =>
        new(poleFeatureId, kind, capturedAtUtc, -23.55, -46.633, [0xFF, 0xD8, 0xFF, 0xD9]);

    private static PoleLicenseRecord License(string poleFeatureId) =>
        new(poleFeatureId, "SCM-123", "Acme Telecom", CutoffUtc.AddYears(-1), CutoffUtc.AddYears(5));

    private static PoleDossierRecord Dossier(string poleFeatureId) =>
        new(poleFeatureId, "Enel", CutoffUtc.AddDays(-1), new DossierResult([]));

    [Fact]
    public void BuildDefenseBundle_EvidenceCapturedExactlyAtCutoff_IsIncluded()
    {
        var repository = new InMemoryPoleAuditEvidenceRepository();
        repository.AddEvidence(Evidence("pole-1", CutoffUtc));

        var bundle = repository.BuildDefenseBundle("pole-1", CutoffUtc);

        bundle.Evidence.Should().ContainSingle();
    }

    [Fact]
    public void BuildDefenseBundle_EvidenceCapturedBeforeCutoff_IsExcluded()
    {
        var repository = new InMemoryPoleAuditEvidenceRepository();
        repository.AddEvidence(Evidence("pole-1", CutoffUtc.AddSeconds(-1)));

        var bundle = repository.BuildDefenseBundle("pole-1", CutoffUtc);

        bundle.Evidence.Should().BeEmpty();
    }

    [Fact]
    public void BuildDefenseBundle_EvidenceCapturedAfterCutoff_IsIncluded()
    {
        var repository = new InMemoryPoleAuditEvidenceRepository();
        repository.AddEvidence(Evidence("pole-1", CutoffUtc.AddSeconds(1)));

        var bundle = repository.BuildDefenseBundle("pole-1", CutoffUtc);

        bundle.Evidence.Should().ContainSingle();
    }

    [Fact]
    public void BuildDefenseBundle_MixOfEvidenceAroundCutoff_OnlyReturnsEntriesAtOrAfterCutoffWhileLicenseAndDossierStayUnfiltered()
    {
        var repository = new InMemoryPoleAuditEvidenceRepository();
        var before = Evidence("pole-1", CutoffUtc.AddDays(-1));
        var atCutoff = Evidence("pole-1", CutoffUtc, PoleAuditEvidenceKind.YellowTagPhoto);
        var after = Evidence("pole-1", CutoffUtc.AddDays(1), PoleAuditEvidenceKind.BandPosition500mmPhoto);
        repository.AddEvidence(before);
        repository.AddEvidence(atCutoff);
        repository.AddEvidence(after);
        repository.AddLicenseRecord(License("pole-1"));
        repository.AddDossierRecord(Dossier("pole-1"));

        var bundle = repository.BuildDefenseBundle("pole-1", CutoffUtc);

        bundle.Evidence.Should().BeEquivalentTo([atCutoff, after]);
        bundle.LicenseRecords.Should().ContainSingle();
        bundle.DossierRecords.Should().ContainSingle();
    }

    [Fact]
    public void BuildDefenseBundle_LicenseRecordIssuedBeforeCutoff_IsStillIncludedUnfiltered()
    {
        var repository = new InMemoryPoleAuditEvidenceRepository();
        var license = License("pole-1");
        repository.AddLicenseRecord(license);

        var bundle = repository.BuildDefenseBundle("pole-1", CutoffUtc.AddYears(10));

        bundle.LicenseRecords.Should().ContainSingle().Which.Should().Be(license);
    }

    [Fact]
    public void BuildDefenseBundle_DossierRecordGeneratedBeforeCutoff_IsStillIncludedUnfiltered()
    {
        var repository = new InMemoryPoleAuditEvidenceRepository();
        var dossier = Dossier("pole-1");
        repository.AddDossierRecord(dossier);

        var bundle = repository.BuildDefenseBundle("pole-1", CutoffUtc.AddYears(10));

        bundle.DossierRecords.Should().ContainSingle().Which.Should().Be(dossier);
    }

    [Fact]
    public void BuildDefenseBundle_TwoDifferentPoles_AreIsolatedFromEachOther()
    {
        var repository = new InMemoryPoleAuditEvidenceRepository();
        repository.AddEvidence(Evidence("pole-a", CutoffUtc));
        repository.AddLicenseRecord(License("pole-a"));
        repository.AddDossierRecord(Dossier("pole-a"));

        var bundleForPoleB = repository.BuildDefenseBundle("pole-b", CutoffUtc);

        bundleForPoleB.Evidence.Should().BeEmpty();
        bundleForPoleB.LicenseRecords.Should().BeEmpty();
        bundleForPoleB.DossierRecords.Should().BeEmpty();
    }

    [Fact]
    public void BuildDefenseBundle_PoleWithNothingAdded_ReturnsEmptyBundleRatherThanThrowing()
    {
        var repository = new InMemoryPoleAuditEvidenceRepository();

        var bundle = repository.BuildDefenseBundle("never-seen-pole", CutoffUtc);

        bundle.PoleFeatureId.Should().Be("never-seen-pole");
        bundle.Evidence.Should().BeEmpty();
        bundle.LicenseRecords.Should().BeEmpty();
        bundle.DossierRecords.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void BuildDefenseBundle_NullOrEmptyPoleFeatureId_ThrowsArgumentException(string? poleFeatureId)
    {
        var repository = new InMemoryPoleAuditEvidenceRepository();

        var act = () => repository.BuildDefenseBundle(poleFeatureId!, CutoffUtc);

        act.Should().Throw<ArgumentException>();
    }
}
