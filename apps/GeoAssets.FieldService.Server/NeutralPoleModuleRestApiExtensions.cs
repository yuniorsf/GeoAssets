using GeoAssets.Domain.NeutralPole.UnauthorizedCableNotice;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Exposes GeoAssets.Domain.NeutralPole's slot allocation and Cabo à Revelia notice workflow
/// (XD01-212) as its own independently-mappable endpoint group — see <c>Program.cs</c>'s
/// <c>Modules:NeutralPole:Enabled</c> toggle. Backed by <see cref="NeutralPoleRegistry"/>'s
/// in-memory, process-lifetime store — see that type's own doc comment for why.
///
/// Deliberately exposes only <c>PoleSlotAllocationBoard.GetOccupancySummary()</c>, never
/// <c>GetAllocations()</c> — the latter's full operator-identified detail is admin/regulator-only
/// per that domain type's doc comment, and this host has no authorization concept yet to gate
/// it behind (XD01-211's AC: "Competitor-tenant data never crosses in query results").
/// </summary>
internal static class NeutralPoleModuleRestApiExtensions
{
    public static IEndpointRouteBuilder MapNeutralPoleModuleApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/field-service/neutral-pole")
    {
        MapSlotEndpoints(routes, prefix);
        MapUnauthorizedCableNoticeEndpoints(routes, prefix);

        return routes;
    }

    private static void MapSlotEndpoints(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapPost($"{prefix}/poles/{{poleId}}/slots/allocate", (string poleId, OperatorSlotRequest request, NeutralPoleRegistry registry) =>
        {
            var board = registry.GetOrCreateBoard(poleId);
            var result = board.Allocate(request.OperatorId);
            return Results.Ok(new SlotAllocationResponseDto(result.Outcome.ToString(), result.SlotIndex));
        })
        .WithName("AllocatePoleSlot")
        .WithSummary("Allocates the lowest free attachment slot on a pole to an operator.");

        routes.MapPost($"{prefix}/poles/{{poleId}}/slots/release", (string poleId, OperatorSlotRequest request, NeutralPoleRegistry registry) =>
        {
            var board = registry.GetOrCreateBoard(poleId);
            var released = board.Release(request.OperatorId);
            return Results.Ok(new { released });
        })
        .WithName("ReleasePoleSlot")
        .WithSummary("Frees whatever slot an operator holds on a pole.");

        routes.MapGet($"{prefix}/poles/{{poleId}}/slots/summary", (string poleId, NeutralPoleRegistry registry) =>
        {
            var board = registry.GetOrCreateBoard(poleId);
            var summary = board.GetOccupancySummary().Select(s => new SlotOccupancySummaryDto(s.SlotIndex, s.IsOccupied)).ToList();
            return Results.Ok(summary);
        })
        .WithName("GetPoleSlotOccupancySummary")
        .WithSummary("Anonymized slot occupancy for a pole — safe for any operator sharing it to see.");
    }

    private static void MapUnauthorizedCableNoticeEndpoints(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapPost($"{prefix}/poles/{{poleId}}/unauthorized-cable-notices", (string poleId, DetectUnauthorizedCableRequest request, NeutralPoleRegistry registry) =>
        {
            var notice = registry.GetOrCreateNotice(poleId, request.DetectedAt);
            return Results.Ok(ToResponse(notice));
        })
        .WithName("DetectUnauthorizedCable")
        .WithSummary("Records an unauthorized cable detected on a pole, starting its Cabo à Revelia notice.");

        routes.MapPost($"{prefix}/poles/{{poleId}}/unauthorized-cable-notices/issue", (string poleId, InstantRequest request, NeutralPoleRegistry registry) =>
        {
            var notice = registry.GetNotice(poleId);
            if (notice is null) return Results.NotFound();

            notice.IssueNotice(request.AsOf);
            return Results.Ok(ToResponse(notice));
        })
        .WithName("IssueUnauthorizedCableNotice")
        .WithSummary("Starts the 30-day cure window for a pole's unauthorized-cable notice.")
        .Produces(StatusCodes.Status404NotFound);

        routes.MapPost($"{prefix}/poles/{{poleId}}/unauthorized-cable-notices/resolve", (string poleId, InstantRequest request, NeutralPoleRegistry registry) =>
        {
            var notice = registry.GetNotice(poleId);
            if (notice is null) return Results.NotFound();

            notice.Resolve(request.AsOf);
            return Results.Ok(ToResponse(notice));
        })
        .WithName("ResolveUnauthorizedCableNotice")
        .WithSummary("Marks a pole's unauthorized cable as regularized or removed.")
        .Produces(StatusCodes.Status404NotFound);
    }

    private static CaboAReveliaNoticeResponse ToResponse(CaboAReveliaNotice notice) => new(
        notice.PoleId, notice.Status.ToString(), notice.DetectedAt, notice.NoticeIssuedAt, notice.NoticeDeadline, notice.ResolvedAt);
}
