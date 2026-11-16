using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace GeoAssets.IpBilling.Server;

/// <summary>
/// Maps the Distribuidora reference-data endpoints under <c>/api/ip-billing/distribuidoras</c>
/// (XD01-301): Distribuidora CRUD and each Distribuidora's lamp wattage chart — the chart
/// Fiscalização/Ofício validation converts inspected points to kWh with. Same
/// authenticated-caller-only bar as every other route in this host (see
/// <see cref="IpBillingRestApiExtensions"/>'s doc comment).
///
/// Both collections are paged: <c>page</c> is 1-based (default 1), <c>pageSize</c> defaults to
/// <see cref="DefaultPageSize"/> and is capped at <see cref="MaxPageSize"/>; a value below 1 for
/// either is a 400. Sort order is the repositories' own deterministic one (Distribuidoras by
/// name then id; chart rows by lamp code, wattage, then id).
/// </summary>
internal static class IpBillingDistribuidoraRestApiExtensions
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    internal sealed record DistribuidoraWriteRequest(Guid OrganizationId, string Name);

    internal sealed record LampWattageUpsertRequest(LampTypeCode Code, int WattageW, decimal BallastLossW);

    internal sealed record PageResponse<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

    public static void MapDistribuidoraApi(RouteGroupBuilder group)
    {
        MapDistribuidoraReads(group);
        MapDistribuidoraWrites(group);
        MapLampWattageChart(group);
    }

    /// <summary>
    /// Resolves the effective 1-based page and capped page size, or <c>false</c> when either
    /// was supplied below 1. Pure, so the paging contract is directly unit-testable.
    /// </summary>
    internal static bool TryResolvePaging(int? page, int? pageSize, out int resolvedPage, out int resolvedPageSize)
    {
        resolvedPage = page ?? 1;
        resolvedPageSize = Math.Min(pageSize ?? DefaultPageSize, MaxPageSize);
        return resolvedPage >= 1 && resolvedPageSize >= 1;
    }

    private static IResult InvalidPaging() =>
        Results.BadRequest(new { message = $"'page' and 'pageSize' must be at least 1 ('pageSize' is capped at {MaxPageSize})." });

    private static void MapDistribuidoraReads(RouteGroupBuilder group)
    {
        group.MapGet("/distribuidoras", async (
            int? page,
            int? pageSize,
            [FromServices] IDistribuidoraRepository repo,
            CancellationToken ct) =>
        {
            if (!TryResolvePaging(page, pageSize, out var resolvedPage, out var resolvedPageSize)) return InvalidPaging();

            var result = await repo.GetPageAsync(resolvedPage, resolvedPageSize, ct).ConfigureAwait(false);
            return Results.Ok(new PageResponse<Distribuidora>(result.Items, result.TotalCount, resolvedPage, resolvedPageSize));
        });

        group.MapGet("/distribuidoras/{id:guid}", async (
            Guid id,
            [FromServices] IDistribuidoraRepository repo,
            CancellationToken ct) =>
        {
            var item = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);
            return item is not null ? Results.Ok(item) : Results.NotFound();
        });
    }

    private static void MapDistribuidoraWrites(RouteGroupBuilder group)
    {
        group.MapPost("/distribuidoras", async (
            [FromBody] DistribuidoraWriteRequest req,
            [FromServices] IDistribuidoraRepository repo,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new { message = "'name' is required." });

            var distribuidora = new Distribuidora { OrganizationId = req.OrganizationId, Name = req.Name.Trim() };
            await repo.AddAsync(distribuidora, ct).ConfigureAwait(false);
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Created($"/api/ip-billing/distribuidoras/{distribuidora.Id}", distribuidora);
        });

        group.MapPut("/distribuidoras/{id:guid}", async (
            Guid id,
            [FromBody] DistribuidoraWriteRequest req,
            [FromServices] IDistribuidoraRepository repo,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new { message = "'name' is required." });

            var distribuidora = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);
            if (distribuidora is null) return Results.NotFound();

            distribuidora.OrganizationId = req.OrganizationId;
            distribuidora.Name = req.Name.Trim();
            await repo.UpdateAsync(distribuidora, ct).ConfigureAwait(false);
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        });

        group.MapDelete("/distribuidoras/{id:guid}", async (
            Guid id,
            [FromServices] IDistribuidoraRepository repo,
            CancellationToken ct) =>
        {
            await repo.DeleteAsync(id, ct).ConfigureAwait(false);
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        });
    }

    private static void MapLampWattageChart(RouteGroupBuilder group)
    {
        group.MapGet("/distribuidoras/{id:guid}/lamp-wattages", async (
            Guid id,
            int? page,
            int? pageSize,
            [FromServices] IDistribuidoraLampWattageRepository repo,
            CancellationToken ct) =>
        {
            if (!TryResolvePaging(page, pageSize, out var resolvedPage, out var resolvedPageSize)) return InvalidPaging();

            var result = await repo.GetPageAsync(id, resolvedPage, resolvedPageSize, ct).ConfigureAwait(false);
            return Results.Ok(new PageResponse<DistribuidoraLampWattage>(result.Items, result.TotalCount, resolvedPage, resolvedPageSize));
        });

        group.MapPut("/distribuidoras/{id:guid}/lamp-wattages", async (
            Guid id,
            [FromBody] LampWattageUpsertRequest req,
            [FromServices] IDistribuidoraRepository distribuidoras,
            [FromServices] IDistribuidoraLampWattageRepository repo,
            CancellationToken ct) =>
        {
            if (!Enum.IsDefined(req.Code)) return Results.BadRequest(new { message = $"'{req.Code}' is not a recognized lamp type." });
            if (req.WattageW <= 0) return Results.BadRequest(new { message = "'wattageW' must be positive." });
            if (req.BallastLossW < 0) return Results.BadRequest(new { message = "'ballastLossW' must not be negative." });

            if (await distribuidoras.GetByIdAsync(id, ct).ConfigureAwait(false) is null) return Results.NotFound();

            var stored = await repo.UpsertAsync(
                new DistribuidoraLampWattage
                {
                    DistribuidoraId = id,
                    Code = req.Code,
                    WattageW = req.WattageW,
                    BallastLossW = req.BallastLossW,
                },
                ct).ConfigureAwait(false);
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Ok(stored);
        });

        // Not nested under its Distribuidora: a chart row's own id identifies it, which is all
        // IDistribuidoraLampWattageRepository.DeleteAsync carries. A missing row is a no-op.
        group.MapDelete("/distribuidoras/lamp-wattages/{rowId:guid}", async (
            Guid rowId,
            [FromServices] IDistribuidoraLampWattageRepository repo,
            CancellationToken ct) =>
        {
            await repo.DeleteAsync(rowId, ct).ConfigureAwait(false);
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        });
    }
}
