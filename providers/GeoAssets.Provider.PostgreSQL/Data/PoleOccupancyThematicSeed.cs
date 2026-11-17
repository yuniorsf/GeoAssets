using System.Globalization;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Provider.PostgreSQL.Entities;

namespace GeoAssets.Provider.PostgreSQL.Data;

/// <summary>
/// The thematic styling of poles by occupancy (XD01-318), seeded with the domain catalog: one
/// point layer per <see cref="OccupancyClass"/>, and one layer rule per class on the seeded pole
/// asset type (<see cref="DomainAssetTypeIds.Pole"/>) whose single condition matches the pole's
/// <see cref="OccupancyAssetAttributes.ClassKey"/> attribute — written by the approval that
/// commits a pole's occupancy. A pole whose occupancy was never committed carries no such
/// attribute, matches no rule, and keeps the pole type's default layer.
///
/// Built-in but not protected, like the rest of the domain catalog: a deployment may restyle
/// or remove them.
/// </summary>
internal static class PoleOccupancyThematicSeed
{
    /// <summary>One class: its layer, rule and condition ids, the rule's priority, and the layer's name and colour.</summary>
    internal sealed record Theme(OccupancyClass Class, Guid LayerId, Guid RuleId, Guid ConditionId, int Priority, string Name, string Color);

    /// <summary>Most severe first; the priority follows, although at most one rule can match a pole.</summary>
    public static IReadOnlyList<Theme> Themes { get; } =
    [
        new(OccupancyClass.Unidentified, Id(1, 6), Id(2, 1), Id(3, 1), 0, "Poste — ocupante no identificado", "#8839ef"),
        new(OccupancyClass.OverCapacity, Id(1, 7), Id(2, 2), Id(3, 2), 1, "Poste — sobre capacidad", "#d20f39"),
        new(OccupancyClass.High, Id(1, 8), Id(2, 3), Id(3, 3), 2, "Poste — ocupación completa", "#df8e1d"),
        new(OccupancyClass.Low, Id(1, 9), Id(2, 4), Id(3, 4), 3, "Poste — ocupación baja", "#40a02b"),
        new(OccupancyClass.None, Id(1, 10), Id(2, 5), Id(3, 5), 4, "Poste — sin ocupantes", "#9ca0b0"),
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
        Attribute = OccupancyAssetAttributes.ClassKey,
        Operator = LayerRuleOperator.Equals,
        Value = OccupancyAssetAttributes.ClassValue(theme.Class),
    });

    /// <summary><c>00000000-0000-0000-{group:x4}-{number:x12}</c>: layers in group 1 beside <see cref="DomainLayerIds"/>, rules in 2, conditions in 3.</summary>
    private static Guid Id(int group, int number) =>
        Guid.Parse(string.Create(CultureInfo.InvariantCulture, $"00000000-0000-0000-{group:x4}-{number:x12}"), CultureInfo.InvariantCulture);
}
