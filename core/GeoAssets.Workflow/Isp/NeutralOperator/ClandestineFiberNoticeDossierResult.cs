using GeoAssets.Core.Isp.Compliance;

namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>Result of <see cref="ClandestineFiberNoticeDossierGenerator.Generate"/> (XD01-263).</summary>
public sealed record ClandestineFiberNoticeDossierResult(
    string DocumentText,
    DateTimeOffset DeadlineUtc,
    IReadOnlyList<DossierFile> AttachmentFiles);
