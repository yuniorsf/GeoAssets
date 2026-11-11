namespace GeoAssets.Core.Isp.NeutralOperator;

/// <summary>
/// One of a pole's up to 6 physical attachment positions in the 500mm telecom band
/// (XD01-258). <see cref="AttachedCableFeatureId"/>, when set, is the <c>GeoFeature.Id</c> of
/// the telecom-cable feature a <c>TopoEdge(Kind = PoleAttachmentSlotMatrix.SupportsEdgeKind)</c>
/// from the owning pole feature points at — <see cref="PoleAttachmentSlotMatrix"/> doesn't
/// create or validate that edge itself (that's the caller's job, against the real
/// <c>IAssetProvider</c>/<c>TopoGraph</c>), it only tracks which slot a given attachment logically
/// occupies.
/// </summary>
public sealed class PoleAttachmentSlot
{
    /// <summary>1-based position within the band, 1-6 inclusive.</summary>
    public required int SlotIndex { get; init; }

    public required PoleAttachmentOccupantStatus Status { get; set; }

    /// <summary>CNPJ of the attaching operator. Required once <see cref="Status"/> is
    /// <see cref="PoleAttachmentOccupantStatus.AuthorizedContracted"/>; otherwise optional
    /// (e.g. unknown for a clandestine attachment pending identification).</summary>
    public string? OperatorCnpj { get; set; }

    /// <summary>ANATEL SCM license number backing the attachment, when known.</summary>
    public string? AnatelLicenseNumber { get; set; }

    /// <summary>Attached cable diameter, in millimeters.</summary>
    public decimal CableDiameterMm { get; set; }

    /// <summary>The attaching operator's declared/rated mechanical load contribution of this
    /// attachment, in daN (decanewtons) — inspector-recorded at census time (XD01-262), not a
    /// value this codebase derives from <see cref="CableDiameterMm"/> via any physics formula.
    /// Defaults to 0 for attachments recorded before this field existed.</summary>
    public decimal CableLoadDaN { get; set; }

    /// <summary>The attached telecom-cable <c>GeoFeature.Id</c>, when this slot is physically occupied.</summary>
    public string? AttachedCableFeatureId { get; set; }
}
