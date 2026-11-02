namespace GeoAssets.IpBilling.Levantamento;

/// <summary>
/// Coverage already billed by a validated Fiscalização or Ofício for one transformer, within
/// some window — what <see cref="IInterveningOperationLookup"/> reports back.
/// </summary>
public sealed record InterveningOperationCoverage(DateOnly ValidatedThrough, decimal AlreadyBilledKwh);
