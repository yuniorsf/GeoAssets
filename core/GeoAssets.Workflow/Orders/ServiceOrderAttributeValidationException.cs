namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Thrown by <see cref="ValidatingServiceOrderRepository"/> when a <see cref="ServiceOrder"/>'s
/// <see cref="IServiceOrder.Attributes"/> fail its order type's
/// <see cref="OrderType.AttributesSchemaJson"/> validation.
/// </summary>
public sealed class ServiceOrderAttributeValidationException(string orderTypeId, IReadOnlyList<string> errors)
    : InvalidOperationException(
        $"ServiceOrder attributes are invalid for order type '{orderTypeId}': {string.Join("; ", errors)}")
{
    public string OrderTypeId { get; } = orderTypeId;
    public IReadOnlyList<string> Errors { get; } = errors;
}
