namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Thrown by <see cref="ValidatingServiceOrderRepository"/> when a <see cref="ServiceOrder"/>'s
/// <see cref="IServiceOrder.Attributes"/> fail its order type's
/// <see cref="OrderType.AttributesSchemaJson"/> validation.
/// </summary>
public sealed class ServiceOrderAttributeValidationException : InvalidOperationException
{
    public string? OrderTypeId { get; }
    public IReadOnlyList<string> Errors { get; }

    public ServiceOrderAttributeValidationException(string orderTypeId, IReadOnlyList<string> errors)
        : base($"ServiceOrder attributes are invalid for order type '{orderTypeId}': {string.Join("; ", errors)}")
    {
        OrderTypeId = orderTypeId;
        Errors = errors;
    }

    public ServiceOrderAttributeValidationException()
    {
        Errors = [];
    }

    public ServiceOrderAttributeValidationException(string message) : base(message)
    {
        Errors = [];
    }

    public ServiceOrderAttributeValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [];
    }
}
