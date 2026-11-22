using System.Text.Json;
using GeoAssets.Identity.Authorization.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace GeoAssets.Server;

/// <summary>
/// The scoped role assignment change feed (XD01-328): <c>GET {prefix}/scoped-role-assignments/sync?after=&amp;take=</c>, the
/// twin of <c>{prefix}/organization-grants/sync</c> (XD01-290) — what a vertical host polls to mirror the roles users hold
/// on other organizations (XD01-325), so it can decide a grant that requires a scoped role (XD01-326) without the identity
/// database. Each entry is resolved to the pair's current state; one with <c>isActive: false</c> is a tombstone
/// (<see cref="GeoAssets.Identity.Authorization.Models.ScopedRoleAssignmentSyncEntry"/>).
///
/// Requires <c>users:read</c>: the feed names users and the roles they hold. <c>take</c> defaults to 500 and is capped at
/// 1000, as for the grant feed.
/// </summary>
internal static class ScopedRoleAssignmentSyncRestApiExtensions
{
    private static readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void MapScopedRoleAssignmentSyncApi(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapGet($"{prefix}/scoped-role-assignments/sync", async (
            long after,
            int take,
            [FromServices] IScopedRoleAssignmentSyncFeed feed,
            CancellationToken ct) =>
        {
            var batch = await feed
                .GetSyncBatchAsync(after, Math.Clamp(take <= 0 ? 500 : take, 1, 1000), ct)
                .ConfigureAwait(false);
            return Results.Json(batch, _opts);
        }).RequireAuthorization("users:read");
    }
}
