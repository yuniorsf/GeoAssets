using System.Text.Json;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Contractors;

/// <summary>
/// Reads which (BoQ item, <see cref="PhotoGatingStage"/>) pairs an order already has captured
/// photo evidence for (XD01-246), from <see cref="IServiceOrder.Attributes"/> — same
/// attribute-bag convention <see cref="ServiceOrderBoqAttributes"/> already documents for
/// <c>boqLines</c>, no dedicated column:
/// <code>
///   Attributes["photoEvidenceStages"] = "[{\"itemCode\":\"US-EL-014\",\"stage\":\"Before\",\"evidenceHash\":\"&lt;base64 HMAC signature&gt;\"}]"
/// </code>
/// The evidence hash itself is whatever <c>EvidenceCaptureResult.Signature</c>
/// (<c>GeoAssets.Shared</c>, XD01-244) produced at capture time — this type only reads the bag,
/// it does not capture or validate a signature itself (that already happened at capture time;
/// re-verification, if ever needed, is <c>EvidenceCaptureService.Verify</c>'s job against the
/// actual photo bytes, not this attribute record).
/// </summary>
public static class ServiceOrderPhotoEvidenceAttributes
{
    public const string EvidenceStagesKey = "photoEvidenceStages";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<(string ItemCode, PhotoGatingStage Stage, string EvidenceHash)> ReadEvidenceStages(
        IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        if (!attributes.TryGetValue(EvidenceStagesKey, out var raw) || string.IsNullOrWhiteSpace(raw))
            return [];

        var parsed = JsonSerializer.Deserialize<List<PhotoEvidenceStageDto>>(raw, JsonOptions)
            ?? throw new InvalidOperationException($"ServiceOrder.Attributes['{EvidenceStagesKey}'] could not be parsed as a photo-evidence-stage array.");

        return parsed.ConvertAll(e => (e.ItemCode, Enum.Parse<PhotoGatingStage>(e.Stage, ignoreCase: true), e.EvidenceHash));
    }

    private sealed record PhotoEvidenceStageDto(string ItemCode, string Stage, string EvidenceHash);
}
