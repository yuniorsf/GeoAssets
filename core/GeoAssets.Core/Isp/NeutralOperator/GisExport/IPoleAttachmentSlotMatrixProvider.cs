namespace GeoAssets.Core.Isp.NeutralOperator.GisExport;

/// <summary>
/// Looks up the persisted <see cref="PoleAttachmentSlotMatrix"/> for a pole, when one exists.
/// Kept separate from <see cref="GisExportProjector"/> so the projector itself doesn't depend on
/// any particular persistence technology (none exists for slot matrices yet as of XD01-271 —
/// see <see cref="NullPoleAttachmentSlotMatrixProvider"/>).
/// </summary>
public interface IPoleAttachmentSlotMatrixProvider
{
    /// <summary>Returns <c>null</c> when no slot matrix is on file for this pole.</summary>
    PoleAttachmentSlotMatrix? GetForPole(string poleFeatureId);
}
