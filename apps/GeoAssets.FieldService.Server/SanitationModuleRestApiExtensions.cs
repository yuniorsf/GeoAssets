using GeoAssets.Domain.Sanitation.Icerp;
using GeoAssets.Domain.Sanitation.ResponseSla;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Exposes GeoAssets.Domain.Sanitation's stateless calculations (XD01-212) as its own
/// independently-mappable endpoint group — see <c>Program.cs</c>'s <c>Modules:Sanitation:Enabled</c>
/// toggle. Every endpoint here is a pure computation over its request body; nothing is
/// persisted, matching the domain assembly's own zero-dependency, zero-state design.
/// </summary>
internal static class SanitationModuleRestApiExtensions
{
    public static IEndpointRouteBuilder MapSanitationModuleApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/field-service/sanitation")
    {
        routes.MapPost($"{prefix}/response-sla/status", (SlaStatusRequest request) =>
        {
            var clock = new RepairResponseClock(new RepairResponseDefinition("requested", request.Deadline), request.StartedAt);
            return Results.Ok(new SlaStatusResponse(
                clock.Status(request.AsOf, request.AtRiskThreshold).ToString(),
                clock.Remaining(request.AsOf),
                clock.PercentElapsed(request.AsOf),
                clock.DueAt));
        })
        .WithName("GetSanitationResponseSlaStatus")
        .WithSummary("Computes an ANA repair-response clock's status for a given deadline and as-of instant.");

        routes.MapPost($"{prefix}/icerp/reinstatement", (IcerpReinstatementRequest request) =>
        {
            var trench = new TrenchDimensions(request.LengthMeters, request.WidthMeters);
            var area = PavementReinstatementCalculator.CalculateReinstatementAreaSquareMeters(trench, request.MarginMeters);
            var volume = CbuqReinstatementCalculator.CalculateVolumeCubicMeters(area, request.ThicknessMeters);

            return Results.Ok(new IcerpReinstatementResponse(area, volume));
        })
        .WithName("CalculateIcerpReinstatement")
        .WithSummary("Computes ICERP pavement reinstatement area and CBUQ asphalt volume for a trench repair.");

        return routes;
    }
}
