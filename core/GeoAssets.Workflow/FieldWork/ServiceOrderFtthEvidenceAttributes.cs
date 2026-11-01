using System.Text.Json;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Reads which <see cref="FtthEvidenceSlot"/>s an FTTH drop-install order already has captured
/// photo evidence for (XD01-278), from <see cref="IServiceOrder.Attributes"/> — same
/// attribute-bag convention <see cref="GeoAssets.Workflow.Contractors.ServiceOrderPhotoEvidenceAttributes"/>
/// already documents for the BoQ-item photo-gating engine:
/// <code>
///   Attributes["ftthEvidenceSlots"] = "[\"CtoConnection\",\"DropAnchorTag\"]"
/// </code>
/// Each slot name is written only once its photo has been captured and HMAC-sealed
/// (<c>EvidenceCaptureService.Capture</c>/<c>EvidenceVerificationService.Verify</c>,
/// <c>GeoAssets.Core</c>) — this type only reads the bag, it does not capture or verify a
/// signature itself.
/// </summary>
public static class ServiceOrderFtthEvidenceAttributes
{
    public const string CapturedSlotsKey = "ftthEvidenceSlots";

    public static IReadOnlySet<FtthEvidenceSlot> ReadCapturedSlots(IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        if (!attributes.TryGetValue(CapturedSlotsKey, out var raw) || string.IsNullOrWhiteSpace(raw))
            return new HashSet<FtthEvidenceSlot>();

        var parsed = JsonSerializer.Deserialize<List<string>>(raw)
            ?? throw new InvalidOperationException($"ServiceOrder.Attributes['{CapturedSlotsKey}'] could not be parsed as a slot-name array.");

        return new HashSet<FtthEvidenceSlot>(parsed.Select(s => Enum.Parse<FtthEvidenceSlot>(s, ignoreCase: true)));
    }

    /// <summary>
    /// Marks <paramref name="slot"/> as captured on <paramref name="order"/> (XD01-277) —
    /// idempotent, a no-op if already recorded. Mutates <see cref="ServiceOrder.Attributes"/>
    /// directly rather than returning a new value, same convention
    /// <c>WaterPavingSubOrderSpawner</c>/<c>FtthDropInstallTransitioner</c> already use for
    /// in-place <see cref="ServiceOrder"/> mutation.
    /// </summary>
    public static void RecordCapturedSlot(ServiceOrder order, FtthEvidenceSlot slot)
    {
        ArgumentNullException.ThrowIfNull(order);

        var current = ReadCapturedSlots(order.Attributes);
        if (current.Contains(slot)) return;

        var updated = new HashSet<FtthEvidenceSlot>(current) { slot };
        order.Attributes[CapturedSlotsKey] = JsonSerializer.Serialize(updated.Select(s => s.ToString()));
    }
}
