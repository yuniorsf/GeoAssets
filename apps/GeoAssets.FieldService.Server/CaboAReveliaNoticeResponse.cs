namespace GeoAssets.FieldService.Server;

internal sealed record CaboAReveliaNoticeResponse(
    string PoleId,
    string Status,
    DateTimeOffset DetectedAt,
    DateTimeOffset? NoticeIssuedAt,
    DateTimeOffset? NoticeDeadline,
    DateTimeOffset? ResolvedAt);
