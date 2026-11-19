using System.Globalization;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Provider.PostgreSQL.Entities;

namespace GeoAssets.Provider.PostgreSQL.Data;

/// <summary>
/// The thematic styling of poles by occupancy (XD01-318), seeded with the domain catalog: one
/// point layer per occupancy class, and one layer rule per class on the seeded pole
/// asset type (<see cref="DomainAssetTypeIds.Pole"/>) whose single condition matches the pole's
/// <see cref="OccupancyClassAttribute"/> attribute — written by the approval that
/// commits a pole's occupancy. A pole whose occupancy was never committed carries no such
/// attribute, matches no rule, and keeps the pole type's default layer.
///
/// Built-in but not protected, like the rest of the domain catalog: a deployment may restyle
/// or remove them.
/// </summary>
internal static class PoleOccupancyThematicSeed
{
    /// <summary>One class: its layer, rule and condition ids, the rule's priority, and the layer's name and colour.</summary>
    internal sealed record Theme(string Class, Guid LayerId, Guid RuleId, Guid ConditionId, int Priority, string Name, string Color);

    /// <summary>
    /// The pole attribute the occupancy approval writes (XD01-318) and the values it takes — the NeutralOperator
    /// vertical's <c>OccupancyAssetAttributes.ClassKey</c> / <c>ClassValue</c>, as seed data (XD01-375: the provider
    /// references no vertical; a test pins these to the vertical's definitions).
    /// </summary>
    internal const string OccupancyClassAttribute = "occupancyClass";

    /// <summary>Most severe first; the priority follows, although at most one rule can match a pole.</summary>
    public static IReadOnlyList<Theme> Themes { get; } =
    [
        new("Unidentified", Id(1, 6), Id(2, 1), Id(3, 1), 0, "Poste — ocupante no identificado", "#8839ef"),
        new("OverCapacity", Id(1, 7), Id(2, 2), Id(3, 2), 1, "Poste — sobre capacidad", "#d20f39"),
        new("High", Id(1, 8), Id(2, 3), Id(3, 3), 2, "Poste — ocupación completa", "#df8e1d"),
        new("Low", Id(1, 9), Id(2, 4), Id(3, 4), 3, "Poste — ocupación baja", "#40a02b"),
        new("None", Id(1, 10), Id(2, 5), Id(3, 5), 4, "Poste — sin ocupantes", "#9ca0b0"),
    ];

    public static IEnumerable<LayerRow> Layers() => Themes.Select(theme => new LayerRow
    {
        Id = theme.LayerId,
        Name = theme.Name,
        GeometryType = GeometryType.Point,
        Color = theme.Color,
        FillColor = theme.Color,
        Radius = 6,
    });

    public static IEnumerable<LayerRuleRow> Rules() => Themes.Select(theme => new LayerRuleRow
    {
        Id = theme.RuleId,
        AssetTypeId = DomainAssetTypeIds.Pole,
        LayerId = theme.LayerId,
        Priority = theme.Priority,
    });

    public static IEnumerable<LayerRuleConditionRow> Conditions() => Themes.Select(theme => new LayerRuleConditionRow
    {
        Id = theme.ConditionId,
        LayerRuleId = theme.RuleId,
        Attribute = OccupancyClassAttribute,
        Operator = LayerRuleOperator.Equals,
        Value = theme.Class,
    });

    /// <summary><c>00000000-0000-0000-{group:x4}-{number:x12}</c>: layers in group 1 beside <see cref="DomainLayerIds"/>, rules in 2, conditions in 3.</summary>
    private static Guid Id(int group, int number) =>
        Guid.Parse(string.Create(CultureInfo.InvariantCulture, $"00000000-0000-0000-{group:x4}-{number:x12}"), CultureInfo.InvariantCulture);
}
