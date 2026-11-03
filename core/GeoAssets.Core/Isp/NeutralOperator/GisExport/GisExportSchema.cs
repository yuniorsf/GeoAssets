namespace GeoAssets.Core.Isp.NeutralOperator.GisExport;

/// <summary>
/// Field-name and version reference for the generic concessionaire GIS export contract
/// (XD01-271) — the versioned, documented projection of pole + attachment-slot + compliance
/// data that <see cref="GisExportProjector"/> produces as a plain RFC 7946 GeoJSON
/// <c>FeatureCollection</c>, consumable by GE Smallworld/Esri ArcGIS Enterprise or any other
/// generic GIS import with zero GeoAssets-specific knowledge. Vendor-specific adapters are
/// explicitly out of scope for this epic.
///
/// <para><b>Contract v1 (<see cref="Version"/>) — one GeoJSON <c>Feature</c> per pole:</b></para>
/// <list type="bullet">
/// <item><see cref="FieldSchemaVersion"/> (string) — this contract's version, always present so a
/// consumer can detect a future breaking change before it causes a silent field-name mismatch.</item>
/// <item><see cref="FieldFeatureId"/> (string) — the pole's <c>GeoFeature.Id</c>.</item>
/// <item><see cref="FieldAssetTypeId"/> (string) — the pole's <c>GeoFeatureProperties.AssetTypeId</c>.</item>
/// <item><see cref="FieldName"/> (string) — the pole's display name.</item>
/// <item><see cref="FieldAttachmentSlotsTotal"/> (number) — count of recorded attachment slots
/// (0-6, see <see cref="PoleAttachmentSlotMatrix.MaxSlots"/>); 0 when no slot matrix is on file
/// for this pole yet.</item>
/// <item><see cref="FieldAttachmentSlotsOccupied"/> (number) — slots whose
/// <see cref="PoleAttachmentOccupantStatus"/> is anything other than vacant/unrecorded.</item>
/// <item><see cref="FieldAttachmentSlotsAvailable"/> (number) — <c>MaxSlots − total</c>, the
/// physical capacity still free regardless of how many of the recorded slots are occupied.</item>
/// <item><see cref="FieldComplianceStatus"/> (string) — one of <c>"compliant"</c> (every recorded
/// slot is <see cref="PoleAttachmentOccupantStatus.AuthorizedContracted"/> or
/// <see cref="PoleAttachmentOccupantStatus.ActiveReservation"/>), <c>"non-compliant"</c> (at
/// least one slot is <see cref="PoleAttachmentOccupantStatus.ClandestineUnauthorized"/> or
/// <see cref="PoleAttachmentOccupantStatus.AbandonedDeadDrop"/>), <c>"no-attachments"</c> (a slot
/// matrix is on file but empty), or <c>"unknown"</c> (no slot matrix on file for this pole).</item>
/// </list>
///
/// Geometry is the pole's own <c>GeoFeature.Geometry</c>, serialized by the standard
/// GeoJSON4STJ <c>GeoJsonConverterFactory</c> — plain GeoJSON, not a GeoAssets-specific shape.
/// </summary>
public static class GisExportSchema
{
    public const string Version = "1";

    public const string FieldSchemaVersion = "schemaVersion";
    public const string FieldFeatureId = "poleFeatureId";
    public const string FieldAssetTypeId = "assetTypeId";
    public const string FieldName = "name";
    public const string FieldAttachmentSlotsTotal = "attachmentSlotsTotal";
    public const string FieldAttachmentSlotsOccupied = "attachmentSlotsOccupied";
    public const string FieldAttachmentSlotsAvailable = "attachmentSlotsAvailable";
    public const string FieldComplianceStatus = "complianceStatus";

    public const string ComplianceCompliant = "compliant";
    public const string ComplianceNonCompliant = "non-compliant";
    public const string ComplianceNoAttachments = "no-attachments";
    public const string ComplianceUnknown = "unknown";
}
