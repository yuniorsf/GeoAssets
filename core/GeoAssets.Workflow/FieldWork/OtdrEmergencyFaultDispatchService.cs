using GeoAssets.Core.Isp.Otdr;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// End-to-end OTDR fault dispatch (XD01-283): resolves an optical distance from either a raw
/// <c>.SOR</c> trace or a direct OLT LOS-alarm value, then hands it to
/// <see cref="IOtdrFaultCorrelationEngine"/> (XD01-282) against the target cable's own geometry.
/// Never throws for a malformed/out-of-range input (AC2) — every failure path returns
/// <see cref="OtdrFaultDispatchOutcome.Failure"/> with a diagnostic instead.
/// </summary>
public sealed class OtdrEmergencyFaultDispatchService(IOtdrFaultCorrelationEngine engine)
{
    public OtdrFaultDispatchOutcome Dispatch(
        OtdrFaultSourceInput source,
        GeoFeature cableFeature,
        double helixFactor,
        double centralOfficeSlackMeters,
        IReadOnlyList<OtdrCableSlackPoint> slackPoints,
        IReadOnlyList<OtdrPoleAlongRoute> polesAlongRoute,
        double spliceMatchToleranceMeters = 1.0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(cableFeature);

        double opticalDistanceMeters;
        try
        {
            opticalDistanceMeters = ResolveOpticalDistanceMeters(source);
        }
        catch (SorTraceParseException ex)
        {
            return OtdrFaultDispatchOutcome.Failure(ex.Message);
        }

        if (cableFeature.Geometry is not GeoLineString cableRoute)
        {
            return OtdrFaultDispatchOutcome.Failure(
                $"Cable feature '{cableFeature.Id}' has no line-string geometry to correlate against.");
        }

        var request = new OtdrFaultCorrelationRequest(
            opticalDistanceMeters, helixFactor, centralOfficeSlackMeters, slackPoints, cableRoute, polesAlongRoute, spliceMatchToleranceMeters);

        try
        {
            return OtdrFaultDispatchOutcome.Success(engine.Correlate(request));
        }
        catch (ArgumentException ex)
        {
            return OtdrFaultDispatchOutcome.Failure(ex.Message);
        }
    }

    private static double ResolveOpticalDistanceMeters(OtdrFaultSourceInput source)
    {
        if (source.SorTraceBytes is not null && source.DirectOpticalDistanceMeters is not null)
            throw new SorTraceParseException("Exactly one of SorTraceBytes or DirectOpticalDistanceMeters must be provided, not both.");

        if (source.SorTraceBytes is { Length: > 0 } bytes)
            return SorTraceParser.Parse(bytes).FaultOpticalDistanceMeters;

        if (source.DirectOpticalDistanceMeters is { } direct)
            return direct;

        throw new SorTraceParseException("Exactly one of SorTraceBytes or DirectOpticalDistanceMeters must be provided.");
    }
}
