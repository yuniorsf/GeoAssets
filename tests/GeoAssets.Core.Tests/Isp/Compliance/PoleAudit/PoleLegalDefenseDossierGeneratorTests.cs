using FluentAssertions;
using GeoAssets.Core.Isp.Compliance;
using GeoAssets.Core.Isp.Compliance.PoleAudit;
using Xunit;

namespace GeoAssets.Core.Tests.Isp.Compliance.PoleAudit;

/// <summary>Covers <see cref="PoleLegalDefenseDossierGenerator.Generate"/> (XD01-286).</summary>
public class PoleLegalDefenseDossierGeneratorTests
{
    private static readonly DateTimeOffset GeneratedAtUtc = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static PoleAuditEvidenceEntry Evidence(
        string poleFeatureId, PoleAuditEvidenceKind kind = PoleAuditEvidenceKind.AnchorPhoto, DateTimeOffset? capturedAtUtc = null) =>
        new(poleFeatureId, kind, capturedAtUtc ?? GeneratedAtUtc, -23.55, -46.633, [0xFF, 0xD8, 0xFF, 0xD9]);

    private static PoleLicenseRecord License(string poleFeatureId, DateTimeOffset? expiresAtUtc = null) =>
        new(poleFeatureId, "SCM-123", "Acme Telecom", GeneratedAtUtc.AddYears(-1), expiresAtUtc);

    private static PoleDossierRecord Dossier(string poleFeatureId, IReadOnlyList<DossierFile> files) =>
        new(poleFeatureId, "Enel", GeneratedAtUtc.AddDays(-1), new DossierResult(files));

    private static PoleDefenseBundle EmptyBundle(string poleFeatureId) =>
        new(poleFeatureId, [], [], []);

    [Fact]
    public void Generate_NullBundles_ThrowsArgumentNullException()
    {
        var act = () => PoleLegalDefenseDossierGenerator.Generate(null!, [], GeneratedAtUtc);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Generate_NullNotFoundPoleFeatureIds_ThrowsArgumentNullException()
    {
        var act = () => PoleLegalDefenseDossierGenerator.Generate([], null!, GeneratedAtUtc);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Generate_BothBundlesAndNotFoundEmpty_ThrowsArgumentException()
    {
        var act = () => PoleLegalDefenseDossierGenerator.Generate([], [], GeneratedAtUtc);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Generate_NotFoundPoleFeatureId_ProducesMissingLineAndItemWithNoAttachments()
    {
        var result = PoleLegalDefenseDossierGenerator.Generate([], ["pole-missing"], GeneratedAtUtc);

        result.DocumentText.Should().Contain("--- Pole pole-missing ---");
        result.DocumentText.Should().Contain("MISSING: Pole feature id does not resolve to a known feature.");
        result.MissingItems.Should().ContainSingle(m =>
            m.PoleFeatureId == "pole-missing" && m.Description == "Pole feature id does not resolve to a known feature.");
        result.AttachmentFiles.Should().BeEmpty();
    }

    [Fact]
    public void Generate_BundleWithEverythingEmpty_ProducesFourMissingItemsAndNoAttachmentsAndUnflaggedDossierGap()
    {
        var bundle = EmptyBundle("pole-1");

        var result = PoleLegalDefenseDossierGenerator.Generate([bundle], [], GeneratedAtUtc);

        result.MissingItems.Should().HaveCount(4);
        result.MissingItems.Should().Contain(m => m.PoleFeatureId == "pole-1" && m.Description.Contains("AnchorPhoto", StringComparison.Ordinal));
        result.MissingItems.Should().Contain(m => m.PoleFeatureId == "pole-1" && m.Description.Contains("YellowTagPhoto", StringComparison.Ordinal));
        result.MissingItems.Should().Contain(m => m.PoleFeatureId == "pole-1" && m.Description.Contains("BandPosition500mmPhoto", StringComparison.Ordinal));
        result.MissingItems.Should().Contain(m => m.PoleFeatureId == "pole-1" && m.Description == "No SCM license record on file for this pole.");
        result.AttachmentFiles.Should().BeEmpty();
        result.DocumentText.Should().Contain("Compliance dossier: none generated for this pole.");
    }

    [Fact]
    public void Generate_EvidenceForOnlyOneOfThreeKinds_ProducesTwoMissingItemsAndAttachmentsForPresentKindOnly()
    {
        var entry1 = Evidence("pole-1", PoleAuditEvidenceKind.AnchorPhoto, GeneratedAtUtc.AddMinutes(-1));
        var entry2 = Evidence("pole-1", PoleAuditEvidenceKind.AnchorPhoto, GeneratedAtUtc);
        var bundle = new PoleDefenseBundle("pole-1", [entry1, entry2], [], []);

        var result = PoleLegalDefenseDossierGenerator.Generate([bundle], [], GeneratedAtUtc);

        result.MissingItems.Should().HaveCount(3);
        result.MissingItems.Should().Contain(m => m.Description.Contains("YellowTagPhoto", StringComparison.Ordinal));
        result.MissingItems.Should().Contain(m => m.Description.Contains("BandPosition500mmPhoto", StringComparison.Ordinal));

        result.AttachmentFiles.Should().HaveCount(2);
        result.AttachmentFiles[0].FileName.Should().Be("pole-1/evidence-AnchorPhoto-0.jpg");
        result.AttachmentFiles[0].Content.Should().BeEquivalentTo(entry1.SignedJpegBytes);
        result.AttachmentFiles[1].FileName.Should().Be("pole-1/evidence-AnchorPhoto-1.jpg");
        result.AttachmentFiles[1].Content.Should().BeEquivalentTo(entry2.SignedJpegBytes);
    }

    [Fact]
    public void Generate_MultipleEvidenceKindsWithEntries_IndexIncrementsAcrossKindsNotPerKind()
    {
        var anchor = Evidence("pole-1", PoleAuditEvidenceKind.AnchorPhoto);
        var yellowTag1 = Evidence("pole-1", PoleAuditEvidenceKind.YellowTagPhoto);
        var yellowTag2 = Evidence("pole-1", PoleAuditEvidenceKind.YellowTagPhoto);
        var band = Evidence("pole-1", PoleAuditEvidenceKind.BandPosition500mmPhoto);
        var bundle = new PoleDefenseBundle("pole-1", [anchor, yellowTag1, yellowTag2, band], [], []);

        var result = PoleLegalDefenseDossierGenerator.Generate([bundle], [], GeneratedAtUtc);

        result.AttachmentFiles.Should().HaveCount(4);
        result.AttachmentFiles.Select(f => f.FileName).Should().Equal(
            "pole-1/evidence-AnchorPhoto-0.jpg",
            "pole-1/evidence-YellowTagPhoto-1.jpg",
            "pole-1/evidence-YellowTagPhoto-2.jpg",
            "pole-1/evidence-BandPosition500mmPhoto-3.jpg");
        result.DocumentText.Split('\n').Count(l => l.Contains("Evidence: ", StringComparison.Ordinal)).Should().Be(4);
        result.MissingItems.Should().ContainSingle(m => m.Description == "No SCM license record on file for this pole.");
    }

    [Fact]
    public void Generate_LicenseListWithAtLeastOneActive_ProducesNoAllExpiredMissingItem()
    {
        var active = License("pole-1", GeneratedAtUtc.AddYears(1));
        var bundle = new PoleDefenseBundle("pole-1", [], [active], []);

        var result = PoleLegalDefenseDossierGenerator.Generate([bundle], [], GeneratedAtUtc);

        result.MissingItems.Should().NotContain(m => m.Description.Contains("all expired", StringComparison.Ordinal));
        result.MissingItems.Should().NotContain(m => m.Description == "No SCM license record on file for this pole.");
    }

    [Fact]
    public void Generate_LicenseListWithNullExpiry_IsTreatedAsActive()
    {
        var neverExpires = License("pole-1", expiresAtUtc: null);
        var bundle = new PoleDefenseBundle("pole-1", [], [neverExpires], []);

        var result = PoleLegalDefenseDossierGenerator.Generate([bundle], [], GeneratedAtUtc);

        result.MissingItems.Should().NotContain(m => m.Description.Contains("all expired", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_LicenseListAllExpired_ProducesAllExpiredMissingItemAndStillListsEveryLicense()
    {
        var expired1 = License("pole-1", GeneratedAtUtc.AddDays(-1));
        var expired2 = License("pole-1", GeneratedAtUtc.AddYears(-2));
        var bundle = new PoleDefenseBundle("pole-1", [], [expired1, expired2], []);

        var result = PoleLegalDefenseDossierGenerator.Generate([bundle], [], GeneratedAtUtc);

        result.MissingItems.Should().ContainSingle(m =>
            m.PoleFeatureId == "pole-1" &&
            m.Description == "SCM license record(s) on file, but none are active as of dossier generation time (all expired).");
        result.AttachmentFiles.Should().HaveCount(2);
        result.AttachmentFiles[0].FileName.Should().Be("pole-1/license-0.txt");
        result.AttachmentFiles[1].FileName.Should().Be("pole-1/license-1.txt");
        result.DocumentText.Should().Contain("License: SCM-123 (Acme Telecom)");
    }

    [Fact]
    public void Generate_MultipleLicenseRecords_ProduceOneAttachmentPerRecordWithKeyFieldsInContent()
    {
        var license1 = new PoleLicenseRecord("pole-1", "SCM-AAA", "Holder A", GeneratedAtUtc.AddYears(-3), GeneratedAtUtc.AddYears(1));
        var license2 = new PoleLicenseRecord("pole-1", "SCM-BBB", "Holder B", GeneratedAtUtc.AddYears(-2), null);
        var bundle = new PoleDefenseBundle("pole-1", [], [license1, license2], []);

        var result = PoleLegalDefenseDossierGenerator.Generate([bundle], [], GeneratedAtUtc);

        result.AttachmentFiles.Should().HaveCount(2);
        var content0 = System.Text.Encoding.UTF8.GetString(result.AttachmentFiles[0].Content);
        content0.Should().Contain("SCM-AAA").And.Contain("Holder A").And.Contain(license1.IssuedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        var content1 = System.Text.Encoding.UTF8.GetString(result.AttachmentFiles[1].Content);
        content1.Should().Contain("SCM-BBB").And.Contain("Holder B");
    }

    [Fact]
    public void Generate_NonEmptyDossierRecordsWithFiles_ProduceOneAttachmentPerFileWithOriginalContentAndNoMissingItem()
    {
        var file1 = new DossierFile("manifest.csv", [1, 2, 3]);
        var file2 = new DossierFile("manifest.xml", [4, 5, 6]);
        var dossierRecord = Dossier("pole-1", [file1, file2]);
        var bundle = new PoleDefenseBundle("pole-1", [], [], [dossierRecord]);

        var result = PoleLegalDefenseDossierGenerator.Generate([bundle], [], GeneratedAtUtc);

        result.AttachmentFiles.Should().HaveCount(2);
        result.AttachmentFiles[0].FileName.Should().Be("pole-1/dossier/manifest.csv");
        result.AttachmentFiles[0].Content.Should().BeEquivalentTo(file1.Content);
        result.AttachmentFiles[1].FileName.Should().Be("pole-1/dossier/manifest.xml");
        result.AttachmentFiles[1].Content.Should().BeEquivalentTo(file2.Content);
        result.MissingItems.Should().NotContain(m => m.Description.Contains("dossier", StringComparison.OrdinalIgnoreCase));
        result.DocumentText.Should().Contain("Compliance dossier: Enel, generated " + dossierRecord.GeneratedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Generate_MultipleBundles_ProduceIndependentPolePrefixedSectionsWithNoCrossPoleLeakage()
    {
        var evidenceA = Evidence("pole-a");
        var evidenceB = Evidence("pole-b", PoleAuditEvidenceKind.YellowTagPhoto);
        var bundleA = new PoleDefenseBundle("pole-a", [evidenceA], [], []);
        var bundleB = new PoleDefenseBundle("pole-b", [evidenceB], [], []);

        var result = PoleLegalDefenseDossierGenerator.Generate([bundleA, bundleB], [], GeneratedAtUtc);

        result.AttachmentFiles.Should().HaveCount(2);
        result.AttachmentFiles[0].FileName.Should().Be("pole-a/evidence-AnchorPhoto-0.jpg");
        result.AttachmentFiles[1].FileName.Should().Be("pole-b/evidence-YellowTagPhoto-0.jpg");
        result.MissingItems.Where(m => m.PoleFeatureId.Equals("pole-a", StringComparison.Ordinal)).Should().HaveCount(3);
        result.MissingItems.Where(m => m.PoleFeatureId.Equals("pole-b", StringComparison.Ordinal)).Should().HaveCount(3);
        result.DocumentText.Should().Contain("--- Pole pole-a ---");
        result.DocumentText.Should().Contain("--- Pole pole-b ---");
    }

    [Fact]
    public void Generate_HeaderPolesCoveredCount_IncludesBundlesAndNotFoundIds()
    {
        var bundle1 = EmptyBundle("pole-1");
        var bundle2 = EmptyBundle("pole-2");

        var result = PoleLegalDefenseDossierGenerator.Generate([bundle1, bundle2], ["pole-missing"], GeneratedAtUtc);

        result.DocumentText.Should().Contain("Poles covered: 3");
    }
}
