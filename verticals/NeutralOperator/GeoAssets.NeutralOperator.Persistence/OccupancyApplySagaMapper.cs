using System.Text.Json;
using System.Text.Json.Serialization;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Persistence.Entities;

namespace GeoAssets.NeutralOperator.Persistence;

/// <summary>
/// Converts between the approval saga (XD01-383) and its EF entities. The batch, the pole snapshots and the conflicting
/// feature ids travel as JSON, enum values by name, like the change sets' changes; the state column is written by name too.
/// </summary>
internal static class OccupancyApplySagaMapper
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        IgnoreReadOnlyProperties = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    public static OccupancyApplySagaRecord ToRecord(OccupancyApplySaga saga) => new()
    {
        BatchId = saga.BatchId,
        ServiceOrderId = saga.OrderId,
        OrderTypeId = saga.OrderTypeId,
        Attempt = saga.Attempt,
        PreviousStatus = saga.PreviousStatus,
        ApprovedStatus = saga.ApprovedStatus,
        RequestedBy = saga.RequestedBy,
        RequestedAt = OccupancyChangeSetMapper.WholeMicroseconds(saga.RequestedAt),
        LastRequestedAt = OccupancyChangeSetMapper.WholeMicroseconds(saga.LastRequestedAt),
        RetryCount = saga.RetryCount,
        TimedOutAt = Stamp(saga.TimedOutAt),
        State = ToStateText(saga.State),
        CompletedAt = Stamp(saga.CompletedAt),
        ReasonCode = saga.ReasonCode,
        Reason = saga.Reason,
        ConflictingFeatureIdsJson = JsonSerializer.Serialize(saga.ConflictingFeatureIds, Json),
        BatchJson = JsonSerializer.Serialize(saga.Batch, Json),
        OrganizationId = saga.OrganizationId,
    };

    public static OccupancyApplySaga ToDomain(OccupancyApplySagaRecord record) => new()
    {
        BatchId = record.BatchId,
        Attempt = record.Attempt,
        OrderId = record.ServiceOrderId,
        OrderTypeId = record.OrderTypeId,
        OrganizationId = record.OrganizationId,
        PreviousStatus = record.PreviousStatus,
        ApprovedStatus = record.ApprovedStatus,
        RequestedBy = record.RequestedBy,
        RequestedAt = Instant(record.RequestedAt),
        LastRequestedAt = Instant(record.LastRequestedAt),
        RetryCount = record.RetryCount,
        TimedOutAt = record.TimedOutAt is { } timedOut ? Instant(timedOut) : null,
        State = FromStateText(record.State),
        CompletedAt = record.CompletedAt is { } completed ? Instant(completed) : null,
        ReasonCode = record.ReasonCode,
        Reason = record.Reason,
        ConflictingFeatureIds = Read<List<string>>(record.ConflictingFeatureIdsJson, "conflicting feature ids"),
        Batch = Read<AssetMutationBatchRequestedEvent>(record.BatchJson, "asset mutation batch"),
    };

    public static OccupancyApplySagaPoleRecord ToRecord(Guid batchId, Guid organizationId, OccupancyApplySagaPole pole) => new()
    {
        BatchId = batchId,
        PoleFeatureId = pole.PoleFeatureId,
        BaselineVersion = pole.BaselineVersion,
        AppliedVersion = pole.AppliedVersion,
        BaselineEntriesJson = JsonSerializer.Serialize(pole.BaselineEntries, Json),
        OrganizationId = organizationId,
    };

    public static OccupancyApplySagaPole ToDomain(OccupancyApplySagaPoleRecord record) => new(
        record.PoleFeatureId,
        record.BaselineVersion,
        record.AppliedVersion,
        Read<List<PoleOccupancyEntry>>(record.BaselineEntriesJson, "pole occupancy entries"));

    public static string ToStateText(OccupancyApplySagaState state) => state switch
    {
        OccupancyApplySagaState.Pending => nameof(OccupancyApplySagaState.Pending),
        OccupancyApplySagaState.Committed => nameof(OccupancyApplySagaState.Committed),
        OccupancyApplySagaState.Reverted => nameof(OccupancyApplySagaState.Reverted),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown saga state."),
    };

    public static string ToJson(IReadOnlyList<string> featureIds) => JsonSerializer.Serialize(featureIds, Json);

    private static OccupancyApplySagaState FromStateText(string state) => state switch
    {
        nameof(OccupancyApplySagaState.Pending) => OccupancyApplySagaState.Pending,
        nameof(OccupancyApplySagaState.Committed) => OccupancyApplySagaState.Committed,
        nameof(OccupancyApplySagaState.Reverted) => OccupancyApplySagaState.Reverted,
        _ => throw new InvalidOperationException($"Stored saga state '{state}' is not a known {nameof(OccupancyApplySagaState)}."),
    };

    private static DateTime? Stamp(DateTimeOffset? instant) =>
        instant is { } value ? OccupancyChangeSetMapper.WholeMicroseconds(value) : null;

    private static DateTimeOffset Instant(DateTime stored) => new(StoredInstant.ToUtc(stored));

    private static T Read<T>(string json, string what)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json)
                ?? throw new InvalidOperationException($"Stored {what} are null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Stored {what} are not valid JSON.", ex);
        }
    }
}
