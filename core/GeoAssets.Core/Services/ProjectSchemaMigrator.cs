using System.Text.Json.Nodes;
using GeoAssets.Core.Models;

namespace GeoAssets.Core.Services;

/// <summary>
/// Upgrades a persisted <see cref="Project"/> JSON payload from an older <see cref="Project.SchemaVersion"/>
/// to the current one. Applied at the deserialize boundary, orthogonal to any EF Core schema migrations —
/// this only concerns the shape of the JSON payload itself.
/// </summary>
public static class ProjectSchemaMigrator
{
    /// <summary>One upgrade step per entry, keyed by the version being upgraded <i>from</i>. Empty for now —
    /// nothing to migrate yet at <see cref="Project.CurrentSchemaVersion"/> 1. Add an entry here (and bump
    /// <see cref="Project.CurrentSchemaVersion"/>) the next time the persisted shape changes.</summary>
    private static readonly Dictionary<int, Func<JsonNode, JsonNode>> Migrations = new();

    /// <summary>Upgrades <paramref name="payload"/> from <paramref name="fromVersion"/> to <paramref name="toVersion"/>,
    /// one registered step at a time. A no-op when the two versions are equal.</summary>
    /// <exception cref="InvalidOperationException">No migration is registered for an intermediate version.</exception>
    public static JsonNode Migrate(JsonNode payload, int fromVersion, int toVersion)
    {
        var current = payload;

        for (var version = fromVersion; version < toVersion; version++)
        {
            if (!Migrations.TryGetValue(version, out var migrate))
                throw new InvalidOperationException(
                    $"No Project schema migration registered to upgrade from version {version}.");

            current = migrate(current);
        }

        return current;
    }
}
