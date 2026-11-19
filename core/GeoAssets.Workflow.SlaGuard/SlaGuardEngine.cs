using System.Globalization;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.SlaGuard;

/// <summary>
/// Statutory deadline engine (XD01-226): looks up a service order's maximum allowed duration
/// from a <see cref="SlaMatrixRow"/> matrix keyed by (service category, territorial setting),
/// computes its deadline, and detects a breach as a typed <see cref="SlaBreachEvent"/>. Pure
/// domain logic — no infrastructure dependency; a host decides what to do with a raised breach
/// (log it, publish it, surface it on a dashboard).
/// </summary>
public sealed class SlaGuardEngine
{
    /// <summary>Well-known <see cref="ServiceOrder.Attributes"/> keys this engine stamps — a host reads these back the same way it reads any other attribute.</summary>
    public const string DeadlineAttributeKey = "slaDeadlineUtc";
    public const string ServiceCategoryAttributeKey = "slaServiceCategory";
    public const string TerritorialSettingAttributeKey = "slaTerritorialSetting";

    private readonly Dictionary<(string ServiceCategory, string TerritorialSetting), TimeSpan> _matrix;

    public SlaGuardEngine(IEnumerable<SlaMatrixRow> matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        _matrix = new Dictionary<(string, string), TimeSpan>();
        foreach (var row in matrix)
            _matrix[(row.ServiceCategory, row.TerritorialSetting)] = row.MaxDuration;
    }

    /// <summary>
    /// The exact max-duration deadline for (<paramref name="serviceCategory"/>,
    /// <paramref name="territorialSetting"/>) — <paramref name="startedAt"/> plus that row's
    /// <see cref="SlaMatrixRow.MaxDuration"/>, nothing more. Throws
    /// <see cref="KeyNotFoundException"/> for a combination the matrix doesn't document, the
    /// same "fail loudly on an unconfigured combination" convention
    /// <see cref="OrderTypeRegistry.Get"/> already uses — a silently-unlimited deadline would be
    /// a worse failure mode for a compliance SLA than an explicit error.
    /// </summary>
    public DateTimeOffset ComputeDeadline(string serviceCategory, string territorialSetting, DateTimeOffset startedAt) =>
        startedAt + MaxDurationFor(serviceCategory, territorialSetting);

    /// <summary>
    /// Stamps the computed deadline (and the category/setting it was computed from) onto
    /// <paramref name="order"/>'s own <see cref="ServiceOrder.Attributes"/> — "attached to
    /// ServiceOrder at creation" per this ticket's own wording, reusing the existing
    /// attributes-bag convention rather than a new field/table.
    /// </summary>
    public void AttachDeadline(ServiceOrder order, string serviceCategory, string territorialSetting, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var deadline = ComputeDeadline(serviceCategory, territorialSetting, timeProvider.GetUtcNow());
        order.Attributes[DeadlineAttributeKey] = deadline.ToString("O", CultureInfo.InvariantCulture);
        order.Attributes[ServiceCategoryAttributeKey] = serviceCategory;
        order.Attributes[TerritorialSettingAttributeKey] = territorialSetting;
    }

    /// <summary>
    /// Null when <paramref name="now"/> has not yet passed the deadline; a populated
    /// <see cref="SlaBreachEvent"/> the instant it has. <paramref name="now"/> is caller-supplied
    /// (not read from a clock here) so a host can check an order's breach status for any point
    /// in time, including a past one during reporting.
    /// </summary>
    public SlaBreachEvent? CheckBreach(
        string orderId, string serviceCategory, string territorialSetting, DateTimeOffset startedAt, DateTimeOffset now)
    {
        var deadline = ComputeDeadline(serviceCategory, territorialSetting, startedAt);
        return now > deadline ? new SlaBreachEvent(orderId, serviceCategory, territorialSetting, deadline, now) : null;
    }

    private TimeSpan MaxDurationFor(string serviceCategory, string territorialSetting) =>
        _matrix.TryGetValue((serviceCategory, territorialSetting), out var maxDuration)
            ? maxDuration
            : throw new KeyNotFoundException(
                $"No SLA matrix row documents service category '{serviceCategory}' for territorial setting '{territorialSetting}'.");
}
