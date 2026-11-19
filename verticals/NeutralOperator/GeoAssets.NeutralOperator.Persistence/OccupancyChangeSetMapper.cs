using System.Text.Json;
using System.Text.Json.Serialization;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Persistence.Entities;

namespace GeoAssets.NeutralOperator.Persistence;

/// <summary>
/// Converts between the domain <see cref="OccupancyChangeSet"/> and its EF entity (XD01-315).
/// The changes travel as one JSON array; enum values are written by name there, and the status
/// column too, so reordering an enum can never re-label stored rows.
/// </summary>
internal static class OccupancyChangeSetMapper
{
    private static readonly JsonSerializerOptions ChangeJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        IgnoreReadOnlyProperties = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    public static OccupancyChangeSetRecord ToRecord(OccupancyChangeSet changeSet) => new()
    {
        Id = changeSet.Id,
        ServiceOrderId = changeSet.OrderId,
        TargetId = changeSet.TargetId,
        PoleFeatureId = changeSet.PoleFeatureId,
        ChangesJson = ToChangesJson(changeSet.Changes),
        BaselineVersion = changeSet.BaselineVersion,
        Status = ToStatusText(changeSet.Status),
        StagedBy = changeSet.StagedBy,
        StagedAt = WholeMicroseconds(changeSet.StagedAt),
        ClosedAt = changeSet.ClosedAt is { } closedAt ? WholeMicroseconds(closedAt) : null,
        OrganizationId = changeSet.PoleOwnerOrganizationId,
    };

    /// <summary>
    /// Rebuilds the set through <see cref="OccupancyChangeSet.Restore"/>, so a row that no
    /// longer forms a valid set fails the read loudly instead of surfacing half-valid.
    /// </summary>
    public static OccupancyChangeSet ToDomain(OccupancyChangeSetRecord record) => OccupancyChangeSet.Restore(
        record.Id,
        record.ServiceOrderId,
        record.TargetId,
        record.PoleFeatureId,
        record.OrganizationId,
        FromChangesJson(record.ChangesJson),
        record.BaselineVersion,
        FromStatusText(record.Status),
        record.StagedBy,
        new DateTimeOffset(StoredInstant.ToUtc(record.StagedAt)),
        record.ClosedAt is { } closedAt ? new DateTimeOffset(StoredInstant.ToUtc(closedAt)) : null);

    public static string ToChangesJson(IReadOnlyList<OccupancyChange> changes) => JsonSerializer.Serialize(changes, ChangeJson);

    public static IReadOnlyList<OccupancyChange> FromChangesJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<OccupancyChange>>(json, ChangeJson)
                ?? throw new InvalidOperationException("Stored occupancy changes are null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Stored occupancy changes are not a valid change list.", ex);
        }
    }

    public static string ToStatusText(OccupancyChangeSetStatus status) => status switch
    {
        OccupancyChangeSetStatus.Staged => nameof(OccupancyChangeSetStatus.Staged),
        OccupancyChangeSetStatus.Committed => nameof(OccupancyChangeSetStatus.Committed),
        OccupancyChangeSetStatus.Discarded => nameof(OccupancyChangeSetStatus.Discarded),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown change set status."),
    };

    public static OccupancyChangeSetStatus FromStatusText(string status) => status switch
    {
        nameof(OccupancyChangeSetStatus.Staged) => OccupancyChangeSetStatus.Staged,
        nameof(OccupancyChangeSetStatus.Committed) => OccupancyChangeSetStatus.Committed,
        nameof(OccupancyChangeSetStatus.Discarded) => OccupancyChangeSetStatus.Discarded,
        _ => throw new InvalidOperationException($"Stored change set status '{status}' is not a known {nameof(OccupancyChangeSetStatus)}."),
    };

    /// <summary>
    /// PostgreSQL keeps microseconds; the set a write returns must equal the set read back
    /// afterwards, so sub-microsecond ticks are dropped before either happens.
    /// </summary>
    public static DateTime WholeMicroseconds(DateTimeOffset instant)
    {
        var utc = instant.UtcDateTime;
        return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMicrosecond), DateTimeKind.Utc);
    }
}
