using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence.Entities;
using GeoAssets.Workflow.Selection;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// Converts between the domain <see cref="ServiceOrder"/> and the EF entity
/// <see cref="ServiceOrderRecord"/> (plus its children).
///
/// Features are stored as a JSON array of IDs. Pass an <see cref="IAssetRepository"/>
/// to <see cref="ToDomain"/> to hydrate them; otherwise <see cref="ServiceOrder.Features"/>
/// will be empty and the IDs are available via <see cref="ServiceOrder.FeatureIds"/>.
/// </summary>
internal static class ServiceOrderMapper
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        WriteIndented               = false,
        DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Domain → EF ───────────────────────────────────────────────────────────

    public static ServiceOrderRecord ToRecord(ServiceOrder order)
    {
        var featureIds = order.Features.Select(f => f.Id).ToArray();

        return new ServiceOrderRecord
        {
            Id              = order.Id,
            Title           = order.Title,
            Description     = order.Description,
            OrderTypeId     = order.OrderTypeId,
            Status          = (int)order.Status,
            Priority        = (int)order.Priority,
            CreatedBy       = order.CreatedBy,
            AssignedTo      = order.AssignedTo,
            CreatedAt       = order.CreatedAt,
            UpdatedAt       = order.UpdatedAt,
            ScheduledAt     = order.ScheduledAt,
            CompletedAt     = order.CompletedAt,
            ParentOrderId   = order.ParentOrderId,
            AttributesJson  = JsonSerializer.Serialize(order.Attributes, _json),
            FeatureIdsJson  = JsonSerializer.Serialize(featureIds, _json),
            SelectionSpecJson = order.SelectionSpec is null
                                ? null
                                : JsonSerializer.Serialize(order.SelectionSpec, _json),
            Dispatches = order.Dispatches.Select(ToDispatchRecord).ToList(),
            ActionLog  = order.ActionLog.Select(ToActionLogRecord).ToList(),
        };
    }

    private static OrderDispatchRecord ToDispatchRecord(OrderDispatch d) => new()
    {
        TargetId     = d.TargetId,
        TargetType   = (int)d.TargetType,
        DispatchedBy = d.DispatchedBy,
        DispatchedAt = d.DispatchedAt,
        Note         = d.Note,
    };

    private static OrderActionLogRecord ToActionLogRecord(OrderActionLog a) => new()
    {
        Action          = (int)a.Action,
        PerformedBy     = a.PerformedBy,
        PerformedAt     = a.PerformedAt,
        Comment         = a.Comment,
        ResultingStatus = a.ResultingStatus.HasValue ? (int)a.ResultingStatus.Value : null,
    };

    // ── EF → Domain ───────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a <see cref="ServiceOrderRecord"/> (with its loaded children) to
    /// a domain <see cref="ServiceOrder"/>.
    /// </summary>
    /// <param name="record">EF entity with Dispatches and ActionLog loaded.</param>
    /// <param name="childIds">IDs of direct child orders (queried separately).</param>
    /// <param name="assets">
    /// Optional repository used to hydrate <see cref="ServiceOrder.Features"/>.
    /// When null the feature list is empty; IDs are still available via
    /// <see cref="ServiceOrder.FeatureIds"/>.
    /// </param>
    public static ServiceOrder ToDomain(
        ServiceOrderRecord record,
        IReadOnlyList<string> childIds,
        IAssetRepository? assets = null)
    {
        var featureIds = DeserializeOrDefault<string[]>(record.FeatureIdsJson, []);
        var attributes = DeserializeOrDefault<Dictionary<string, string>>(record.AttributesJson, []);
        var spec       = record.SelectionSpecJson is null
                         ? null
                         : JsonSerializer.Deserialize<FeatureSelectionSpec>(record.SelectionSpecJson, _json);

        var features = new List<GeoFeature>();
        if (assets is not null && featureIds.Length > 0)
        {
            foreach (var fid in featureIds)
            {
                var f = assets.GetById(fid);
                if (f is not null) features.Add(f);
            }
        }

        var order = new ServiceOrder
        {
            Id            = record.Id,
            Title         = record.Title,
            Description   = record.Description,
            OrderTypeId   = record.OrderTypeId,
            Status        = (ServiceOrderStatus)record.Status,
            Priority      = (ServiceOrderPriority)record.Priority,
            CreatedBy     = record.CreatedBy,
            AssignedTo    = record.AssignedTo,
            CreatedAt     = record.CreatedAt,
            UpdatedAt     = record.UpdatedAt,
            ScheduledAt   = record.ScheduledAt,
            CompletedAt   = record.CompletedAt,
            ParentOrderId = record.ParentOrderId,
            SelectionSpec = spec,
            FeatureIds    = featureIds,
        };

        order.Attributes.Clear();
        foreach (var kv in attributes) order.Attributes[kv.Key] = kv.Value;

        order.Features.AddRange(features);

        order.ChildOrderIds.AddRange(childIds);

        order.Dispatches.AddRange(record.Dispatches
            .OrderBy(d => d.DispatchedAt)
            .Select(d => new OrderDispatch(
                d.TargetId,
                (DispatchTargetType)d.TargetType,
                d.DispatchedBy,
                d.DispatchedAt,
                d.Note)));

        order.ActionLog.AddRange(record.ActionLog
            .OrderBy(a => a.PerformedAt)
            .Select(a => new OrderActionLog(
                (OrderActionType)a.Action,
                a.PerformedBy,
                a.PerformedAt,
                a.Comment,
                a.ResultingStatus.HasValue ? (ServiceOrderStatus)a.ResultingStatus.Value : null)));

        return order;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static T DeserializeOrDefault<T>(string? json, T fallback) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return fallback;
        try { return JsonSerializer.Deserialize<T>(json, _json) ?? fallback; }
        catch { return fallback; }
    }
}
