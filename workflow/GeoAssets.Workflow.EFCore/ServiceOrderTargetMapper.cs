using System.Text.Json;
using GeoAssets.Workflow.Persistence.Entities;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// Converts between the domain <see cref="ServiceOrderTarget"/> and the EF entity
/// <see cref="ServiceOrderTargetRecord"/>. <see cref="ServiceOrderTargetRecord.ReleasedAt"/>, and the
/// ledger's <see cref="ServiceOrderTargetRecord.OrderTypeId"/> and <see cref="ServiceOrderTargetRecord.Exclusivity"/>
/// (XD01-379), have no domain counterpart: <see cref="ToRecord"/> takes them as arguments and
/// <see cref="ToDomain"/> drops them.
/// </summary>
internal static class ServiceOrderTargetMapper
{
    public static ServiceOrderTargetRecord ToRecord(
        ServiceOrderTarget target,
        DateTime? releasedAt = null,
        string? orderTypeId = null,
        OrderTargetExclusivity? exclusivity = null) => new()
    {
        Id = target.Id,
        ServiceOrderId = target.OrderId,
        VerticalId = target.VerticalId,
        KernelOrderId = TargetLedger.IsKernel(target.VerticalId) ? target.OrderId : null,
        OrderTypeId = orderTypeId,
        Exclusivity = exclusivity is { } level ? (int)level : null,
        FeatureId = target.FeatureId,
        AssetTypeId = target.AssetTypeId,
        Alias = target.Alias,
        ActivityIdsJson = ToActivityIdsJson(target.ActivityIds),
        Status = target.Status,
        ConflictLevel = (int)target.ConflictLevel,
        Note = target.Note,
        Longitude = target.Longitude,
        Latitude = target.Latitude,
        AddedAt = ToUtc(target.AddedAt),
        AddedBy = target.AddedBy,
        OrganizationId = target.OrganizationId,
        Version = target.Version,
        ReleasedAt = releasedAt,
    };

    public static ServiceOrderTarget ToDomain(ServiceOrderTargetRecord record) => new()
    {
        Id = record.Id,
        OrderId = record.ServiceOrderId,
        VerticalId = record.VerticalId,
        FeatureId = record.FeatureId,
        AssetTypeId = record.AssetTypeId,
        Alias = record.Alias,
        ActivityIds = FromActivityIdsJson(record.ActivityIdsJson),
        Status = record.Status,
        ConflictLevel = (OrderTargetConflictLevel)record.ConflictLevel,
        Note = record.Note,
        Longitude = record.Longitude,
        Latitude = record.Latitude,
        AddedAt = ToUtc(record.AddedAt),
        AddedBy = record.AddedBy,
        OrganizationId = record.OrganizationId,
        Version = record.Version,
    };

    /// <summary>A JSON array of strings, in the given order (never <c>;</c>-separated text).</summary>
    public static string ToActivityIdsJson(IReadOnlyList<string>? activityIds) =>
        JsonSerializer.Serialize(activityIds ?? []);

    /// <summary>
    /// Reads what <see cref="ToActivityIdsJson"/> wrote. A blank column, JSON <c>null</c>, text
    /// that is not a JSON array of strings, and null elements all read as "no activity" rather
    /// than failing the whole page a target is listed on.
    /// </summary>
    public static IReadOnlyList<string> FromActivityIdsJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            var ids = JsonSerializer.Deserialize<List<string?>>(json);
            return ids is null ? [] : ids.OfType<string>().ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// The domain value is a UTC instant. SQLite hands it back with
    /// <see cref="DateTimeKind.Unspecified"/> and PostgreSQL's <c>timestamp with time zone</c>
    /// rejects anything but <see cref="DateTimeKind.Utc"/>, so both directions normalise the kind.
    /// </summary>
    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
