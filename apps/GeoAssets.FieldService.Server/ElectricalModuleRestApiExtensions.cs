using GeoAssets.Domain.Electrical.Continuity;
using GeoAssets.Domain.Electrical.Sla;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Exposes GeoAssets.Domain.Electrical's stateless calculations (XD01-212) as its own
/// independently-mappable endpoint group — see <c>Program.cs</c>'s <c>Modules:Electrical:Enabled</c>
/// toggle. Every endpoint here is a pure computation over its request body; nothing is persisted,
/// matching the domain assembly's own zero-dependency, zero-state design.
/// </summary>
internal static class ElectricalModuleRestApiExtensions
{
    public static IEndpointRouteBuilder MapElectricalModuleApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/field-service/electrical")
    {
        routes.MapPost($"{prefix}/sla/status", (SlaStatusRequest request) =>
        {
            var clock = new SlaClock(new SlaDefinition("requested", request.Deadline), request.StartedAt);
            return Results.Ok(new SlaStatusResponse(
                clock.Status(request.AsOf, request.AtRiskThreshold).ToString(),
                clock.Remaining(request.AsOf),
                clock.PercentElapsed(request.AsOf),
                clock.DueAt));
        })
        .WithName("GetElectricalSlaStatus")
        .WithSummary("Computes an ANEEL REN 1.000 SLA clock's status for a given deadline and as-of instant.");

        routes.MapPost($"{prefix}/continuity/indicators", (ContinuityIndicatorsRequest request) =>
        {
            var events = request.Events.Select(e => new InterruptionEvent(e.ConsumerUnitId, e.StartedAt, e.EndedAt));
            var indicators = ContinuityIndicatorsCalculator.Calculate(
                events, request.ConsumerUnitId, request.PeriodStart, request.PeriodEndExclusive, request.MinimumQualifyingDuration);

            return Results.Ok(new ContinuityIndicatorsResponse(indicators.Dic, indicators.Fic, indicators.Dmic));
        })
        .WithName("CalculateElectricalContinuityIndicators")
        .WithSummary("Computes PRODIST Módulo 8 DIC/FIC/DMIC for one consumer unit over an apuração period.");

        return routes;
    }
}
