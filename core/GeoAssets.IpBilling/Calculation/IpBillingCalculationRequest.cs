namespace GeoAssets.IpBilling.Calculation;

public sealed record IpBillingCalculationRequest
{
    public string? CodigoInstalacao { get; init; }

    public IReadOnlyList<IpLampLineItem> NormalLamps { get; init; } = [];

    public DateOnly? ReferenceDate { get; init; }

    public string? PerformedBy { get; init; }
}
