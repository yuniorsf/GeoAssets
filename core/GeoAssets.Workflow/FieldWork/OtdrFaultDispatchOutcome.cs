using GeoAssets.Core.Services;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>Outcome of <see cref="OtdrEmergencyFaultDispatchService.Dispatch"/> (XD01-283).</summary>
public sealed record OtdrFaultDispatchOutcome(bool Succeeded, string? FailureReason, OtdrFaultCorrelationResult? Result)
{
    public static OtdrFaultDispatchOutcome Success(OtdrFaultCorrelationResult result) => new(true, null, result);

    public static OtdrFaultDispatchOutcome Failure(string reason) => new(false, reason, null);
}
