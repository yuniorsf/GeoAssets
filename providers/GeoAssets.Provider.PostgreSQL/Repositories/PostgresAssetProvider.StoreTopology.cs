using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// Topology traversals in PostgreSQL for a store-served organization (XD01-449), over the rows
/// <see cref="OrganizationAssetCatalog"/> combines — the organization's own and the shared (<see cref="Guid.Empty"/>) ones,
/// never another organization's — with <see cref="TopoGraph"/>'s semantics: a start the caller cannot see has no
/// neighbours, descendants or ancestors; an edge counts only when its target is visible; neighbours keep the start's edge
/// order (duplicates included); descendants follow edges of the kind asked (every kind when none) and ancestors every
/// edge backwards, the start excluded unless a cycle leads back to it, in ordinal id order. A walk reaching more than
/// <see cref="OrganizationCatalogOptions.EnumerationLimit"/> features is refused with
/// <see cref="CatalogEnumerationLimitException"/>. Ancestors use <c>IX_geo_entity_topology</c> (GIN, jsonb_path_ops).
/// </summary>
public sealed partial class PostgresAssetProvider
{
    private const string DefaultEdgeKind = "generic";
    private const string GetDescendantsOperation = "GetDescendants";
    private const string GetAncestorsOperation = "GetAncestors";

    internal IReadOnlyList<GeoFeature> StoreNeighbors(string featureId, string? edgeKind, string? targetAssetTypeId)
    {
        ArgumentNullException.ThrowIfNull(featureId);
        var start = VisibleByIds([featureId]);
        if (start.Count == 0)
            return [];

        var edges = start[0].Topology
            .Where(e => edgeKind is null || string.Equals(e.Kind, edgeKind, StringComparison.Ordinal))
            .ToList();
        var targets = VisibleByIds([.. edges.Select(e => e.TargetId).Distinct(StringComparer.Ordinal)])
            .ToDictionary(f => f.Id, StringComparer.Ordinal);

        return
        [
            .. edges
                .Where(e => targets.ContainsKey(e.TargetId))
                .Select(e => targets[e.TargetId])
                .Where(f => targetAssetTypeId is null || string.Equals(f.Properties.AssetTypeId, targetAssetTypeId, StringComparison.Ordinal)),
        ];
    }

    internal IReadOnlyList<GeoFeature> StoreDescendants(string featureId, string? edgeKind, string? targetAssetTypeId)
    {
        ArgumentNullException.ThrowIfNull(featureId);
        var organizations = VisibleOrganizationIds();
        var kind = edgeKind ?? string.Empty;
        var limit = _readLimits!.EffectiveEnumerationLimit;
        var probe = limit + 1;

        var ids = WithReadContext(db => db.Database.SqlQuery<string>($"""
            WITH RECURSIVE reached(id) AS (
                SELECT edge ->> 'targetId'
                FROM geo_entity s
                CROSS JOIN LATERAL jsonb_array_elements(s.topology) AS edge
                WHERE s."Id" = {featureId} AND s."OrganizationId" = ANY({organizations})
                  AND ({kind} = '' OR COALESCE(edge ->> 'kind', {DefaultEdgeKind}) = {kind})
                UNION
                SELECT edge ->> 'targetId'
                FROM reached r
                JOIN geo_entity s ON s."Id" = r.id AND s."OrganizationId" = ANY({organizations})
                CROSS JOIN LATERAL jsonb_array_elements(s.topology) AS edge
                WHERE ({kind} = '' OR COALESCE(edge ->> 'kind', {DefaultEdgeKind}) = {kind})
            )
            SELECT r.id AS "Value"
            FROM reached r
            WHERE EXISTS (SELECT 1 FROM geo_entity g WHERE g."Id" = r.id AND g."OrganizationId" = ANY({organizations}))
            LIMIT {probe}
            """).ToList());

        if (ids.Count > limit)
            throw new CatalogEnumerationLimitException(GetDescendantsOperation, limit);

        return
        [
            .. VisibleByIds(ids)
                .Where(f => targetAssetTypeId is null || string.Equals(f.Properties.AssetTypeId, targetAssetTypeId, StringComparison.Ordinal))
                .OrderBy(f => f.Id, StringComparer.Ordinal),
        ];
    }

    internal IReadOnlyList<GeoFeature> StoreAncestors(string featureId)
    {
        ArgumentNullException.ThrowIfNull(featureId);
        var organizations = VisibleOrganizationIds();
        var limit = _readLimits!.EffectiveEnumerationLimit;
        var probe = limit + 1;

        var ids = WithReadContext(db => db.Database.SqlQuery<string>($"""
            WITH RECURSIVE reached(id) AS (
                SELECT p."Id"
                FROM geo_entity p
                WHERE p."OrganizationId" = ANY({organizations})
                  AND p.topology @> jsonb_build_array(jsonb_build_object('targetId', {featureId}::text))
                  AND EXISTS (SELECT 1 FROM geo_entity s WHERE s."Id" = {featureId} AND s."OrganizationId" = ANY({organizations}))
                UNION
                SELECT p."Id"
                FROM reached r
                JOIN geo_entity p ON p."OrganizationId" = ANY({organizations})
                  AND p.topology @> jsonb_build_array(jsonb_build_object('targetId', r.id))
            )
            SELECT id AS "Value" FROM reached
            LIMIT {probe}
            """).ToList());

        if (ids.Count > limit)
            throw new CatalogEnumerationLimitException(GetAncestorsOperation, limit);

        return [.. VisibleByIds(ids).OrderBy(f => f.Id, StringComparer.Ordinal)];
    }

    /// <summary>The visible features among <paramref name="ids"/>: the organization's own and the shared ones.</summary>
    private List<GeoFeature> VisibleByIds(List<string> ids)
    {
        if (ids.Count == 0)
            return [];

        var rows = WithReadContext(db => QueryableRows(db).AsNoTracking().Where(r => ids.Contains(r.Id)).ToList());
        return [.. rows.Select(MapToFeature)];
    }

    private Guid[] VisibleOrganizationIds() =>
        _organization is null ? [Guid.Empty] : [_organization.CurrentOrganizationId, Guid.Empty];
}
