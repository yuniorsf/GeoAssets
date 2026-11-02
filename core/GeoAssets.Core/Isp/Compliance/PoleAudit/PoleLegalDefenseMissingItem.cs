namespace GeoAssets.Core.Isp.Compliance.PoleAudit;

/// <summary>
/// One explicitly-flagged gap in a pole's legal defense dossier (XD01-285's AC3, carried forward
/// into XD01-286's compiled document) — e.g. no sealed tag photo on file, no license record, or
/// a requested pole id that doesn't resolve to a real feature at all. Flagged rather than silently
/// omitted so a reviewer knows exactly what evidence still needs to be gathered before relying on
/// the dossier in an actual injunction defense.
/// </summary>
public sealed record PoleLegalDefenseMissingItem(string PoleFeatureId, string Description);
