using GeoAssets.Core.Models;

namespace GeoAssets.Examples;

/// <summary>Console output helpers shared by all examples.</summary>
internal static class Print
{
    internal static void Section(string title)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n  ▶ {title}");
        Console.ResetColor();
    }

    internal static void Feature(GeoFeature f, string? extra = null)
    {
        var geom  = f.Geometry?.GetType().Name ?? "—";
        var edges = f.Topology.Count > 0
            ? $"  →  [{string.Join(", ", f.Topology.Select(e => $"{e.TargetId[..6]}… ({e.Kind} w={e.Weight})"))}]"
            : string.Empty;
        var line  = $"      [{f.Id[..6]}…]  {f.Properties.Name,-22}  ({geom}){edges}";
        if (extra is not null) line += $"  {extra}";
        Console.WriteLine(line);
    }

    internal static void List(string label, IReadOnlyList<GeoFeature> features)
    {
        Console.WriteLine($"      {label} ({features.Count}):");
        if (features.Count == 0) { Console.ForegroundColor = ConsoleColor.DarkGray; Console.WriteLine("        (empty)"); Console.ResetColor(); return; }
        foreach (var f in features) Feature(f);
    }

    internal static void Path(IReadOnlyList<GeoFeature> path)
    {
        if (path.Count == 0) { Console.ForegroundColor = ConsoleColor.DarkGray; Console.WriteLine("      (no path found)"); Console.ResetColor(); return; }
        Console.WriteLine("      Path: " + string.Join(" → ", path.Select(f => f.Properties.Name)));
    }

    internal static void Bool(string label, bool value)
    {
        Console.ForegroundColor = value ? ConsoleColor.Red : ConsoleColor.Green;
        Console.WriteLine($"      {label}: {value}");
        Console.ResetColor();
    }

    internal static void Components(IReadOnlyList<IReadOnlyList<GeoFeature>> components)
    {
        Console.WriteLine($"      Connected components ({components.Count}):");
        for (var i = 0; i < components.Count; i++)
            Console.WriteLine($"        [{i + 1}] " + string.Join(", ", components[i].Select(f => f.Properties.Name)));
    }
}
