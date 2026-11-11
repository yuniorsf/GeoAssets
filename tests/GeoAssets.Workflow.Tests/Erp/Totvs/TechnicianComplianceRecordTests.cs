using FluentAssertions;
using GeoAssets.Workflow.Erp.Totvs;
using Xunit;

namespace GeoAssets.Workflow.Tests.Erp.Totvs;

public class TechnicianComplianceRecordTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private static TechnicianCertificationStatus Cert(TechnicianCertificationType type, DateTime expiresOnUtc) => new(type, expiresOnUtc);

    private static IReadOnlyList<TechnicianCertificationStatus> AllCurrentCertifications() =>
    [
        Cert(TechnicianCertificationType.Nr10Electrical, Now.AddDays(30)),
        Cert(TechnicianCertificationType.Nr35WorkAtHeight, Now.AddDays(30)),
        Cert(TechnicianCertificationType.AsoMedicalClearance, Now.AddDays(30)),
    ];

    [Fact]
    public void IsCompliantAt_AllCertificationsCurrentNoEquipment_ReturnsTrue()
    {
        var record = new TechnicianComplianceRecord("tech-1", AllCurrentCertifications(), [], Now);

        record.IsCompliantAt(Now).Should().BeTrue();
    }

    [Fact]
    public void IsCompliantAt_OneCertificationExpired_ReturnsFalse()
    {
        var certifications = new List<TechnicianCertificationStatus>(AllCurrentCertifications())
        {
            [1] = Cert(TechnicianCertificationType.Nr35WorkAtHeight, Now.AddDays(-1)),
        };
        var record = new TechnicianComplianceRecord("tech-1", certifications, [], Now);

        record.IsCompliantAt(Now).Should().BeFalse();
    }

    [Fact]
    public void IsCompliantAt_MissingRequiredCertification_ReturnsFalse()
    {
        var certifications = AllCurrentCertifications().Where(c => c.Type != TechnicianCertificationType.AsoMedicalClearance).ToList();
        var record = new TechnicianComplianceRecord("tech-1", certifications, [], Now);

        record.IsCompliantAt(Now).Should().BeFalse();
    }

    [Fact]
    public void IsCompliantAt_ExpiryExactlyAtInstant_IsTreatedAsExpired()
    {
        var certifications = new List<TechnicianCertificationStatus>(AllCurrentCertifications())
        {
            [0] = Cert(TechnicianCertificationType.Nr10Electrical, Now),
        };
        var record = new TechnicianComplianceRecord("tech-1", certifications, [], Now);

        record.IsCompliantAt(Now).Should().BeFalse();
    }

    [Fact]
    public void IsCompliantAt_ExpiredEquipmentCalibration_ReturnsFalse()
    {
        var equipment = new List<EquipmentCalibrationStatus> { new("meter-1", Now.AddDays(-1)) };
        var record = new TechnicianComplianceRecord("tech-1", AllCurrentCertifications(), equipment, Now);

        record.IsCompliantAt(Now).Should().BeFalse();
    }

    [Fact]
    public void IsCompliantAt_CurrentEquipmentCalibration_ReturnsTrue()
    {
        var equipment = new List<EquipmentCalibrationStatus> { new("meter-1", Now.AddDays(1)) };
        var record = new TechnicianComplianceRecord("tech-1", AllCurrentCertifications(), equipment, Now);

        record.IsCompliantAt(Now).Should().BeTrue();
    }
}
