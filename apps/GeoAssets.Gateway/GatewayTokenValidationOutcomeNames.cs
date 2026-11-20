namespace GeoAssets.Gateway;

/// <summary>Each <see cref="GatewayTokenValidationOutcome"/>'s name — the <c>validation</c> tag of <see cref="GatewayTelemetry.RateLimitPartitionCounterName"/> (XD01-398).</summary>
internal static class GatewayTokenValidationOutcomeNames
{
    public static string ToName(this GatewayTokenValidationOutcome outcome) => outcome switch
    {
        GatewayTokenValidationOutcome.Absent => "absent",
        GatewayTokenValidationOutcome.Valid => "valid",
        GatewayTokenValidationOutcome.Invalid => "invalid",
        GatewayTokenValidationOutcome.Unavailable => "unavailable",
        GatewayTokenValidationOutcome.Unvalidated => "unvalidated",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown token validation outcome."),
    };
}
