using System.Globalization;
using System.Text;
using System.Text.Json;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// The <c>NOTIFY</c> payload of a <see cref="TileVersionChange"/> (XD01-458): JSON
/// <c>{"o":origin,"g":{scope(N):generation},"f":flushes,"r":[[minLon,minLat,maxLon,maxLat,minZoom,maxZoom]]}</c>, at most
/// <see cref="MaxBytes"/> UTF-8 bytes (PostgreSQL refuses a payload of 8,000 bytes or more). A change too large is sent with
/// its regions merged into one (their union, at their zooms) and, if still too large, as <c>{"o":origin,"x":1}</c> —
/// incomplete, so a listener re-reads the versions and treats every tile as dirty. A payload that can't be read is
/// incomplete too.
/// </summary>
internal static class TileVersionNotification
{
    public const int MaxBytes = 7_900;

    public static string Encode(TileVersionChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.IsComplete)
        {
            var whole = Write(change, change.Regions);
            if (Encoding.UTF8.GetByteCount(whole) <= MaxBytes) return whole;

            if (change.Regions.Count > 1)
            {
                var merged = Write(change, [Union(change.Regions)]);
                if (Encoding.UTF8.GetByteCount(merged) <= MaxBytes) return merged;
            }
        }

        return Write(change.Origin);
    }

    public static TileVersionChange Decode(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var origin = root.GetProperty("o").GetString() ?? string.Empty;
            if (root.TryGetProperty("x", out _)) return Incomplete(origin);

            var generations = new Dictionary<Guid, long>();
            foreach (var scope in root.GetProperty("g").EnumerateObject())
                generations[Guid.ParseExact(scope.Name, "N")] = scope.Value.GetInt64();

            long? flushes = root.TryGetProperty("f", out var f) ? f.GetInt64() : null;
            var regions = new List<DirtyTileRegion>();
            foreach (var r in root.GetProperty("r").EnumerateArray())
            {
                regions.Add(new DirtyTileRegion(
                    new BoundingBox(r[0].GetDouble(), r[1].GetDouble(), r[2].GetDouble(), r[3].GetDouble()), r[4].GetInt32(), r[5].GetInt32()));
            }

            return new TileVersionChange(origin, generations, flushes, regions);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException
                                       or ArgumentException or IndexOutOfRangeException)
        {
            return Incomplete(string.Empty);
        }
    }

    private static TileVersionChange Incomplete(string origin) =>
        new(origin, new Dictionary<Guid, long>(), null, [], IsComplete: false);

    private static DirtyTileRegion Union(IReadOnlyList<DirtyTileRegion> regions) =>
        new(
            new BoundingBox(
                regions.Min(r => r.Bounds.MinLon), regions.Min(r => r.Bounds.MinLat), regions.Max(r => r.Bounds.MaxLon), regions.Max(r => r.Bounds.MaxLat)),
            regions.Min(r => r.MinZoom),
            regions.Max(r => r.MaxZoom));

    private static string Write(TileVersionChange change, IEnumerable<DirtyTileRegion> regions)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("o", change.Origin);
            json.WriteStartObject("g");
            foreach (var (scope, generation) in change.Generations)
                json.WriteNumber(scope.ToString("N", CultureInfo.InvariantCulture), generation);
            json.WriteEndObject();
            if (change.Flushes is { } flushes) json.WriteNumber("f", flushes);
            json.WriteStartArray("r");
            foreach (var region in regions)
            {
                json.WriteStartArray();
                json.WriteNumberValue(region.Bounds.MinLon);
                json.WriteNumberValue(region.Bounds.MinLat);
                json.WriteNumberValue(region.Bounds.MaxLon);
                json.WriteNumberValue(region.Bounds.MaxLat);
                json.WriteNumberValue(region.MinZoom);
                json.WriteNumberValue(region.MaxZoom);
                json.WriteEndArray();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string Write(string origin)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("o", origin);
            json.WriteNumber("x", 1);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
