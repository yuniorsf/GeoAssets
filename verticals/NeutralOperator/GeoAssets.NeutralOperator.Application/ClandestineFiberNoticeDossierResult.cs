using GeoAssets.NeutralOperator.Domain.Compliance;

namespace GeoAssets.NeutralOperator.Application;

/// <summary>Result of <see cref="ClandestineFiberNoticeDossierGenerator.Generate"/> (XD01-263).</summary>
public sealed record ClandestineFiberNoticeDossierResult(
    string DocumentText,
    DateTimeOffset DeadlineUtc,
    IReadOnlyList<DossierFile> AttachmentFiles);
