namespace GeoAssets.Core.Isp.NeutralOperator.GisExport;

/// <summary>
/// No-op default for hosts without a persisted slot-matrix store yet — <see cref="PoleAttachmentSlotMatrix"/>
/// (XD01-258) has no EFCore repository as of this ticket (XD01-271). Every pole exports with
/// <see cref="GisExportSchema.ComplianceUnknown"/> rather than failing the whole export job.
/// </summary>
public sealed class NullPoleAttachmentSlotMatrixProvider : IPoleAttachmentSlotMatrixProvider
{
    public static readonly NullPoleAttachmentSlotMatrixProvider Instance = new();

    private NullPoleAttachmentSlotMatrixProvider()
    {
    }

    public PoleAttachmentSlotMatrix? GetForPole(string poleFeatureId) => null;
}
