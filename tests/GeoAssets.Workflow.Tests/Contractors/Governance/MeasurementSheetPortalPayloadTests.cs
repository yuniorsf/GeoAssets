using FluentAssertions;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Contractors.Governance;
using Xunit;

namespace GeoAssets.Workflow.Tests.Contractors.Governance;

public class MeasurementSheetPortalPayloadTests
{
    private static EspelhoDeMedicaoDossierResult Dossier(Guid orgId) => new(
        ServiceOrderId: "order-1",
        ContractorOrganizationId: orgId,
        ContractorName: "Empreiteira Teste",
        ServiceOrderTitle: "Troca de poste",
        ExecutionStartedAtUtc: new DateTime(2026, 1, 10, 8, 0, 0, DateTimeKind.Utc),
        ExecutionCompletedAtUtc: new DateTime(2026, 1, 20, 17, 0, 0, DateTimeKind.Utc),
        Items:
        [
            new EspelhoDeMedicaoItemLine("US-001", Quantity: 2m, UnitUs: 1.5m, TotalUs: 3m, MonetaryRate: 10m, TotalBrl: 30m, EvidencePhotoHashes: []),
        ],
        TotalBrl: 30m,
        RetentionRate: 0.1m,
        RetentionBrl: 3m,
        NetPayableBrl: 27m,
        AprChecklistSignedAtUtc: null,
        DocumentText: "ESPELHO DE MEDIÇÃO",
        SignatureBase64: "c2lnbmF0dXJl");

    [Fact]
    public void FromDossier_MapsEveryFieldFromTheSignedDossier()
    {
        var orgId = Guid.NewGuid();
        var dossier = Dossier(orgId);

        var payload = MeasurementSheetPortalPayload.FromDossier(dossier, contractId: "contract-99");

        payload.ContractId.Should().Be("contract-99");
        payload.ServiceOrderId.Should().Be(dossier.ServiceOrderId);
        payload.ContractorOrganizationId.Should().Be(orgId);
        payload.ContractorName.Should().Be(dossier.ContractorName);
        payload.TotalBrl.Should().Be(dossier.TotalBrl);
        payload.RetentionBrl.Should().Be(dossier.RetentionBrl);
        payload.NetPayableBrl.Should().Be(dossier.NetPayableBrl);
        payload.DossierSignatureBase64.Should().Be(dossier.SignatureBase64);

        payload.Items.Should().HaveCount(1);
        payload.Items[0].ItemCode.Should().Be("US-001");
        payload.Items[0].Quantity.Should().Be(2m);
        payload.Items[0].UnitUs.Should().Be(1.5m);
        payload.Items[0].TotalBrl.Should().Be(30m);
    }

    [Fact]
    public void FromDossier_NullDossier_Throws()
    {
        var act = () => MeasurementSheetPortalPayload.FromDossier(null!, "contract-1");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FromDossier_EmptyContractId_Throws()
    {
        var act = () => MeasurementSheetPortalPayload.FromDossier(Dossier(Guid.NewGuid()), "");

        act.Should().Throw<ArgumentException>();
    }
}
