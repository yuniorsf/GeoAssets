using GeoAssets.Core.Interfaces;
using GeoAssets.Workflow.Contractors;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Maps the contractor US ledger REST API (XD01-207) onto any <see cref="IEndpointRouteBuilder"/>,
/// backed by whatever <see cref="IContractorUsLedgerRepository"/> is registered in DI.
///
/// The tenant boundary is enforced end-to-end without any route-level organization parameter:
/// every read is pre-filtered by <see cref="ServiceOrderDbContext"/>'s global query filter plus
/// the database-level Row Level Security policy (XD01-202), and <see cref="MapCreate"/> stamps
/// <see cref="ContractorUsLedgerEntry.OrganizationId"/> from the caller's own
/// <see cref="ITenantContext.CurrentOrganizationId"/> — never from the request body — so a
/// caller can't claim a ledger entry under a different contractor's tenant even if they tried.
/// A cross-tenant id (GET/PUT/DELETE by id) looks identical to an unknown one: 404, not 403 —
/// the repository's own filter can't tell the two apart, and leaking "it exists, just not
/// yours" is itself a cross-tenant disclosure.
/// </summary>
internal static class ContractorUsLedgerRestApiExtensions
{
    public static IEndpointRouteBuilder MapContractorUsLedgerApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/fieldservice/contractor-us-ledger")
    {
        MapReads(routes, prefix);
        MapCreate(routes, prefix);
        MapUpdate(routes, prefix);
        MapDelete(routes, prefix);

        return routes;
    }

    private static void MapReads(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapGet(prefix, async (IContractorUsLedgerRepository repo, CancellationToken ct) =>
        {
            var entries = await repo.GetAllAsync(ct).ConfigureAwait(false);
            return Results.Ok(entries.Select(ContractorUsLedgerEntryResponse.FromDomain).ToList());
        })
        .WithName("ListContractorUsLedgerEntries")
        .WithSummary("Lists every US ledger entry for the caller's own contractor organization.")
        .Produces<List<ContractorUsLedgerEntryResponse>>();

        routes.MapGet($"{prefix}/{{id:guid}}", async (Guid id, IContractorUsLedgerRepository repo, CancellationToken ct) =>
        {
            var entry = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);
            return entry is null ? Results.NotFound() : Results.Ok(ContractorUsLedgerEntryResponse.FromDomain(entry));
        })
        .WithName("GetContractorUsLedgerEntry")
        .WithSummary("Gets one US ledger entry by id, scoped to the caller's own contractor organization.")
        .Produces<ContractorUsLedgerEntryResponse>()
        .Produces(StatusCodes.Status404NotFound);

        routes.MapGet($"{prefix}/by-service-order/{{serviceOrderId}}",
            async (string serviceOrderId, IContractorUsLedgerRepository repo, CancellationToken ct) =>
            {
                var entries = await repo.GetByServiceOrderIdAsync(serviceOrderId, ct).ConfigureAwait(false);
                return Results.Ok(entries.Select(ContractorUsLedgerEntryResponse.FromDomain).ToList());
            })
        .WithName("ListContractorUsLedgerEntriesByServiceOrder")
        .WithSummary("Lists US ledger entries claimed against one ServiceOrder.")
        .Produces<List<ContractorUsLedgerEntryResponse>>();

        routes.MapGet($"{prefix}/espelho-medicao",
            async (int year, int month, IContractorUsLedgerRepository repo, CancellationToken ct) =>
            {
                if (month < 1 || month > 12) return Results.BadRequest("month must be between 1 and 12.");

                var from = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
                var toExclusive = from.AddMonths(1);

                var entries = await repo.GetByPeriodAsync(from, toExclusive, ct).ConfigureAwait(false);
                var responses = entries.Select(ContractorUsLedgerEntryResponse.FromDomain).ToList();

                return Results.Ok(new EspelhoDeMedicaoResponse(year, month, responses.Sum(e => e.TotalValue), responses));
            })
        .WithName("GetContractorUsLedgerEspelhoDeMedicao")
        .WithSummary("Monthly \"Espelho de Medição\" statement — the caller's own ledger entries for one calendar month, plus their summed value.")
        .Produces<EspelhoDeMedicaoResponse>()
        .Produces(StatusCodes.Status400BadRequest);
    }

    private static void MapCreate(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapPost(prefix, async (
            CreateContractorUsLedgerEntryRequest request,
            IContractorUsLedgerRepository repo,
            ITenantContext tenantContext,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var entry = new ContractorUsLedgerEntry
            {
                OrganizationId = tenantContext.CurrentOrganizationId,
                ServiceOrderId = request.ServiceOrderId,
                UsCode = request.UsCode,
                Description = request.Description,
                Quantity = request.Quantity,
                UnitValue = request.UnitValue,
                PhotoProof = request.PhotoProof.Select(p => p.ToDomain()).ToList(),
                CreatedAt = now,
            };

            await repo.AddAsync(entry, ct).ConfigureAwait(false);
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);

            return Results.Created(
                new Uri($"{prefix}/{entry.Id}", UriKind.Relative),
                ContractorUsLedgerEntryResponse.FromDomain(entry));
        })
        .WithName("CreateContractorUsLedgerEntry")
        .WithSummary("Creates a US ledger entry under the caller's own contractor organization.")
        .Produces<ContractorUsLedgerEntryResponse>(StatusCodes.Status201Created);
    }

    private static void MapUpdate(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapPut($"{prefix}/{{id:guid}}", async (
            Guid id,
            UpdateContractorUsLedgerEntryRequest request,
            IContractorUsLedgerRepository repo,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var existing = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);
            if (existing is null) return Results.NotFound();

            existing.ServiceOrderId = request.ServiceOrderId;
            existing.UsCode = request.UsCode;
            existing.Description = request.Description;
            existing.Quantity = request.Quantity;
            existing.UnitValue = request.UnitValue;
            existing.PhotoProof.Clear();
            existing.PhotoProof.AddRange(request.PhotoProof.Select(p => p.ToDomain()));
            existing.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

            await repo.UpdateAsync(existing, ct).ConfigureAwait(false);
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);

            return Results.Ok(ContractorUsLedgerEntryResponse.FromDomain(existing));
        })
        .WithName("UpdateContractorUsLedgerEntry")
        .WithSummary("Replaces a US ledger entry's editable fields, scoped to the caller's own contractor organization.")
        .Produces<ContractorUsLedgerEntryResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }

    private static void MapDelete(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapDelete($"{prefix}/{{id:guid}}", async (Guid id, IContractorUsLedgerRepository repo, CancellationToken ct) =>
        {
            await repo.DeleteAsync(id, ct).ConfigureAwait(false);
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithName("DeleteContractorUsLedgerEntry")
        .WithSummary("Deletes a US ledger entry. A no-op (still 204) if the id doesn't exist or belongs to another contractor organization.")
        .Produces(StatusCodes.Status204NoContent);
    }
}
