namespace GeoAssets.Core.Isp.Compliance.PoleAudit;

/// <summary>Result of <see cref="PoleLegalDefenseDossierGenerator.Generate"/> (XD01-286).</summary>
public sealed record PoleLegalDefenseDossierResult(
    string DocumentText,
    IReadOnlyList<PoleLegalDefenseMissingItem> MissingItems,
    IReadOnlyList<ConcessionaireDossierFile> AttachmentFiles);
