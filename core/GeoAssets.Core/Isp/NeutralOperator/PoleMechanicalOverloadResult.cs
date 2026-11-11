namespace GeoAssets.Core.Isp.NeutralOperator;

/// <summary>
/// <paramref name="IsOverloaded"/> plus the inputs behind it, so a caller surfacing the
/// <see cref="PoleMechanicalOverloadCalculator.FlaggedAttributeKey"/> alert can also report the
/// numbers an engineering reviewer would want.
/// </summary>
public sealed record PoleMechanicalOverloadResult(bool IsOverloaded, decimal TotalAuthorizedLoadDaN, decimal RatedCapacityDaN);
