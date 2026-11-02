namespace GeoAssets.Core.Services;

/// <summary>Pure, side-effect-free OTDR trace-to-coordinate fault correlation (XD01-282). See <see cref="OtdrFaultCorrelationEngine"/> for the algorithm.</summary>
public interface IOtdrFaultCorrelationEngine
{
    OtdrFaultCorrelationResult Correlate(OtdrFaultCorrelationRequest request);
}
