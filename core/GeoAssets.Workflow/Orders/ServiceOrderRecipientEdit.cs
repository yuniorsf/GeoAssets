namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Which parts of an order the organization it was dispatched to may change when the order's
/// type lets it edit the order (<see cref="OrderRecipientAccess.Edit"/>, XD01-333).
///
/// A recipient edits the order's content: title, description, priority, schedule and
/// attributes. It cannot change what the order is or where it stands — its type (which would
/// also change what the recipient may do), its status (moved by actions, each checked on its
/// own), who it is assigned to, when it was completed, which order it belongs under — nor the
/// attributes that say who the order is for and which grants cover it.
/// </summary>
public static class ServiceOrderRecipientEdit
{
    /// <summary>
    /// Attributes a recipient may not set, change or remove: the scope a cross-organization grant
    /// is matched against, and the occupant a shared-use notice was fanned out to.
    /// </summary>
    public static IReadOnlyList<string> ReservedAttributeKeys { get; } = ["scopeId", Isp.NeutralOperator.SharedUse.SharedUseDispatchPlanner.PartyOrganizationAttribute];

    /// <summary>
    /// The names of the fields <paramref name="replacement"/> changes that a recipient may not,
    /// in a fixed order; empty when it changes none of them. Text is compared ordinally.
    /// </summary>
    public static IReadOnlyList<string> FixedFieldsChanged(IServiceOrder stored, IServiceOrder replacement)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(replacement);

        var changed = new List<string>();
        if (!string.Equals(stored.OrderTypeId, replacement.OrderTypeId, StringComparison.Ordinal)) changed.Add(nameof(IServiceOrder.OrderTypeId));
        if (!string.Equals(stored.Status, replacement.Status, StringComparison.Ordinal)) changed.Add(nameof(IServiceOrder.Status));
        if (!string.Equals(stored.AssignedTo, replacement.AssignedTo, StringComparison.Ordinal)) changed.Add(nameof(IServiceOrder.AssignedTo));
        if (stored.CompletedAt != replacement.CompletedAt) changed.Add(nameof(IServiceOrder.CompletedAt));
        if (!string.Equals(stored.ParentOrderId, replacement.ParentOrderId, StringComparison.Ordinal)) changed.Add(nameof(IServiceOrder.ParentOrderId));

        foreach (var key in ReservedAttributeKeys)
        {
            var before = stored.Attributes.GetValueOrDefault(key);
            var after = replacement.Attributes.GetValueOrDefault(key);
            if (!string.Equals(before, after, StringComparison.Ordinal)) changed.Add($"{nameof(IServiceOrder.Attributes)}.{key}");
        }

        return changed;
    }
}
