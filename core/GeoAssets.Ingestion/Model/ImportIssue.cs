namespace GeoAssets.Ingestion.Model;

/// <summary>
/// A finding of inventory, mapping, resolution or validation. <see cref="Code"/> is a stable lower-kebab code from
/// <see cref="ImportIssueCodes"/>; <see cref="Subject"/> names the element (its <see cref="SourceKey"/>) or the file.
/// </summary>
public sealed record ImportIssue(IssueSeverity Severity, string Code, string Subject, string Message);
