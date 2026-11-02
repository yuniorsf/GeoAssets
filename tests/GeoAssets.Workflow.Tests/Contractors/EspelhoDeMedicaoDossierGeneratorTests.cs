using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Contractors;

public class EspelhoDeMedicaoDossierGeneratorTests
{
    private const string TenantSecret = "test-tenant-secret";

    private static ServiceOrder Order(string id = "order-1", Guid? organizationId = null, DateTime? completedAt = null) => new()
    {
        Id = id,
        Title = "Troca de poste",
        OrganizationId = organizationId ?? Guid.NewGuid(),
        CreatedAt = new DateTime(2026, 1, 10, 8, 0, 0, DateTimeKind.Utc),
        CompletedAt = completedAt,
    };

    private static ContractorUsPosting Posting(string orderId, Guid contractorOrgId, string itemCode = "US-001") => new()
    {
        ServiceOrderId = orderId,
        ContractorOrganizationId = contractorOrgId,
        ItemCode = itemCode,
        Quantity = 2m,
        UnitUs = 1.5m,
        MonetaryRate = 10m,
        PostedAtUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc),
    };

    private static ContractorUsLedgerEntry LedgerEntry(string orderId, Guid orgId, string usCode, params LedgerPhotoProof[] photos)
    {
        var entry = new ContractorUsLedgerEntry
        {
            OrganizationId = orgId,
            ServiceOrderId = orderId,
            UsCode = usCode,
        };
        entry.PhotoProof.AddRange(photos);
        return entry;
    }

    [Fact]
    public void Generate_ReproducesEveryPostingAndTotal()
    {
        var orgId = Guid.NewGuid();
        var order = Order(organizationId: orgId, completedAt: new DateTime(2026, 1, 20, 17, 0, 0, DateTimeKind.Utc));
        var posting = Posting(order.Id, orgId);

        var result = EspelhoDeMedicaoDossierGenerator.Generate(
            order, "Empreiteira Teste", [posting], [], retentionRate: 0.1m, TenantSecret);

        result.ServiceOrderId.Should().Be(order.Id);
        result.ContractorOrganizationId.Should().Be(orgId);
        result.ContractorName.Should().Be("Empreiteira Teste");
        result.ServiceOrderTitle.Should().Be("Troca de poste");
        result.ExecutionStartedAtUtc.Should().Be(order.CreatedAt);
        result.ExecutionCompletedAtUtc.Should().Be(order.CompletedAt);

        result.Items.Should().ContainSingle();
        var item = result.Items[0];
        item.ItemCode.Should().Be("US-001");
        item.TotalBrl.Should().Be(posting.TotalBrl);

        result.TotalBrl.Should().Be(posting.TotalBrl);
        result.RetentionRate.Should().Be(0.1m);
        result.RetentionBrl.Should().Be(Math.Round(posting.TotalBrl * 0.1m, 2, MidpointRounding.AwayFromZero));
        result.NetPayableBrl.Should().Be(result.TotalBrl - result.RetentionBrl);
    }

    [Fact]
    public void Generate_EmbedsEvidencePhotoHashesPerItem()
    {
        var orgId = Guid.NewGuid();
        var order = Order(organizationId: orgId);
        var posting = Posting(order.Id, orgId);
        var photo = new LedgerPhotoProof("https://example.com/photo.jpg", new GeoPoint(-43.2, -22.9), new DateTime(2026, 1, 16, 9, 0, 0, DateTimeKind.Utc));
        var entry = LedgerEntry(order.Id, orgId, "US-001", photo);

        var result = EspelhoDeMedicaoDossierGenerator.Generate(
            order, "Empreiteira", [posting], [entry], retentionRate: 0m, TenantSecret);

        result.Items[0].EvidencePhotoHashes.Should().ContainSingle();
        result.DocumentText.Should().Contain("Evidence hash:");
    }

    [Fact]
    public void Generate_NoEvidence_FlagsMissingInDocumentText()
    {
        var orgId = Guid.NewGuid();
        var order = Order(organizationId: orgId);
        var posting = Posting(order.Id, orgId);

        var result = EspelhoDeMedicaoDossierGenerator.Generate(
            order, "Empreiteira", [posting], [], retentionRate: 0m, TenantSecret);

        result.Items[0].EvidencePhotoHashes.Should().BeEmpty();
        result.DocumentText.Should().Contain("MISSING: no photo evidence on file");
    }

    [Fact]
    public void Generate_CalledTwiceWithSameInputs_ProducesIdenticalSignedArtifact()
    {
        var orgId = Guid.NewGuid();
        var order = Order(organizationId: orgId);
        var posting = Posting(order.Id, orgId);
        var photo = new LedgerPhotoProof("https://example.com/photo.jpg", new GeoPoint(-43.2, -22.9), new DateTime(2026, 1, 16, 9, 0, 0, DateTimeKind.Utc));
        var entry = LedgerEntry(order.Id, orgId, "US-001", photo);

        var first = EspelhoDeMedicaoDossierGenerator.Generate(order, "Empreiteira", [posting], [entry], 0.05m, TenantSecret);
        var second = EspelhoDeMedicaoDossierGenerator.Generate(order, "Empreiteira", [posting], [entry], 0.05m, TenantSecret);

        second.DocumentText.Should().Be(first.DocumentText);
        second.SignatureBase64.Should().Be(first.SignatureBase64);
    }

    [Fact]
    public void Generate_DifferentTenantSecret_ProducesDifferentSignature()
    {
        var orgId = Guid.NewGuid();
        var order = Order(organizationId: orgId);
        var posting = Posting(order.Id, orgId);

        var first = EspelhoDeMedicaoDossierGenerator.Generate(order, "Empreiteira", [posting], [], 0m, TenantSecret);
        var second = EspelhoDeMedicaoDossierGenerator.Generate(order, "Empreiteira", [posting], [], 0m, "different-secret");

        second.SignatureBase64.Should().NotBe(first.SignatureBase64);
        second.DocumentText.Should().Be(first.DocumentText);
    }

    [Fact]
    public void Generate_NullContractorName_FallsBackToOrganizationId()
    {
        var orgId = Guid.NewGuid();
        var order = Order(organizationId: orgId);

        var result = EspelhoDeMedicaoDossierGenerator.Generate(order, null, [], [], 0m, TenantSecret);

        result.ContractorName.Should().Be(orgId.ToString());
    }

    [Fact]
    public void Generate_MultiplePostings_SortedDeterministicallyByItemCode()
    {
        var orgId = Guid.NewGuid();
        var order = Order(organizationId: orgId);
        var postingB = Posting(order.Id, orgId, "US-002");
        var postingA = Posting(order.Id, orgId, "US-001");

        var result = EspelhoDeMedicaoDossierGenerator.Generate(order, "Empreiteira", [postingB, postingA], [], 0m, TenantSecret);

        result.Items.Select(i => i.ItemCode).Should().Equal("US-001", "US-002");
    }
}
