namespace GeoAssets.Workflow.Erp.Totvs;

/// <summary>
/// A technician's compliance snapshot as of the last nightly Totvs Protheus sync (XD01-253).
/// <see cref="IsCompliantAt"/> is the single predicate <c>TechnicianComplianceTransitionGuard</c>
/// gates APR submission on: every one of <see cref="TechnicianCertificationType"/>'s three
/// required certifications must be present and unexpired, and no assigned equipment's
/// calibration may be expired, as of the instant being checked.
/// </summary>
public sealed record TechnicianComplianceRecord(
    string TechnicianId,
    IReadOnlyList<TechnicianCertificationStatus> Certifications,
    IReadOnlyList<EquipmentCalibrationStatus> EquipmentCalibrations,
    DateTime SyncedAtUtc)
{
    private static readonly TechnicianCertificationType[] RequiredCertifications =
    [
        TechnicianCertificationType.Nr10Electrical,
        TechnicianCertificationType.Nr35WorkAtHeight,
        TechnicianCertificationType.AsoMedicalClearance,
    ];

    public bool IsCompliantAt(DateTime atUtc)
    {
        foreach (var required in RequiredCertifications)
        {
            var status = Certifications.FirstOrDefault(c => c.Type == required);
            if (status is null || status.ExpiresOnUtc <= atUtc) return false;
        }

        foreach (var equipment in EquipmentCalibrations)
        {
            if (equipment.ExpiresOnUtc <= atUtc) return false;
        }

        return true;
    }
}
