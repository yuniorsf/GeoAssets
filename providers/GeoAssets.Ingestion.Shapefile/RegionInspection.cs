using System.Security.Cryptography;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles;
using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Ingestion.Shapefile;

/// <summary>Checks the layer file sets of one region folder (see <see cref="ShapefileDeliveryInventory"/>).</summary>
internal sealed class RegionInspection(
    string region,
    IReadOnlyList<string> files,
    ISourceProfile profile,
    Dictionary<string, string> firstRegionByHash,
    List<ImportIssue> issues)
{
    private static readonly string[] LayerExtensions = [".shp", ".shx", ".dbf", ".prj", ".cpg"];

    private readonly HashSet<string> _pairedOrphans = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<LayerFileSet>> InspectAsync(CancellationToken cancellationToken)
    {
        var layers = new List<(LayerDefinition Definition, string Stem, LayerFileSet Layer)>();
        foreach (var definition in profile.Layers)
        {
            var stems = MatchingStems(definition);
            if (stems.Count == 0)
                continue;

            foreach (var ignored in stems.Skip(1))
                Report(IssueSeverity.Warning, ImportIssueCodes.DuplicateFile, SubjectFileOf(ignored),
                    $"Also names the {definition.Name} layer, which uses '{stems[0]}' in this region; this file set is ignored.");

            var layerFiles = FilesOf(stems[0]);
            layers.Add((definition, stems[0], await InspectLayerAsync(definition, stems[0], layerFiles, cancellationToken).ConfigureAwait(false)));
        }

        // Orphan tables go first to the layer whose stem they start with, then to any layer by record count alone.
        var orphanFor = new Dictionary<int, string>();
        foreach (var byName in new[] { true, false })
        {
            for (var i = 0; i < layers.Count; i++)
            {
                var (_, stem, layer) = layers[i];
                if (layer.Verdict == LayerVerdict.Unusable || layer.AttributeTablePath is not null || orphanFor.ContainsKey(i))
                    continue;
                if (FindOrphanTable(layer.RecordCount, byName ? stem : null) is { } orphan)
                {
                    _pairedOrphans.Add(orphan);
                    orphanFor[i] = orphan;
                }
            }
        }

        var result = new List<LayerFileSet>(layers.Count);
        for (var i = 0; i < layers.Count; i++)
        {
            var (definition, stem, layer) = layers[i];
            result.Add(layer.Verdict == LayerVerdict.Unusable
                ? layer
                : await PairAttributesAsync(definition, stem, layer, orphanFor.GetValueOrDefault(i), cancellationToken).ConfigureAwait(false));
        }

        return result;
    }

    private List<string> MatchingStems(LayerDefinition definition) =>
        files
            .Where(file => LayerExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(stem => LayerStemMatcher.Matches(definition, stem))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();

    private List<string> FilesOf(string stem) =>
        files
            .Where(file => string.Equals(Path.GetFileNameWithoutExtension(file), stem, StringComparison.OrdinalIgnoreCase)
                && LayerExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .ToList();

    private string SubjectFileOf(string stem)
    {
        var stemFiles = FilesOf(stem);
        return Path.GetFileName(Find(stemFiles, ".shp") ?? stemFiles.Order(StringComparer.Ordinal).First());
    }

    private async Task<LayerFileSet> InspectLayerAsync(LayerDefinition definition, string stem, List<string> layerFiles, CancellationToken cancellationToken)
    {
        var shp = Find(layerFiles, ".shp");
        var shx = Find(layerFiles, ".shx");
        var dbf = Find(layerFiles, ".dbf");
        var prj = Find(layerFiles, ".prj");
        var cpg = Find(layerFiles, ".cpg");

        var layer = new LayerFileSet
        {
            Layer = definition.Name,
            Region = region,
            GeometryPath = shp,
            IndexPath = shx,
            AttributeTablePath = dbf,
            CrsWkt = prj is null ? null : (await File.ReadAllTextAsync(prj, cancellationToken).ConfigureAwait(false)).Trim(),
            Encoding = await ReadEncodingAsync(cpg, cancellationToken).ConfigureAwait(false),
        };

        if (shp is null)
        {
            Report(IssueSeverity.Error, ImportIssueCodes.MissingSidecar, stem + ".shp", "The layer has no .shp geometry file; it cannot be imported.");
            return layer with { RecordCount = dbf is null ? 0 : DbaseHeaderInfo.Read(dbf).RecordCount, Verdict = LayerVerdict.Unusable };
        }

        layer = layer with { RecordCount = CountRecords(shp, shx, dbf) };
        var hash = Convert.ToHexString(await HashAsync(shp, cancellationToken).ConfigureAwait(false));
        if (firstRegionByHash.TryGetValue(hash, out var firstRegion) && !string.Equals(firstRegion, region, StringComparison.Ordinal))
        {
            Report(IssueSeverity.Warning, ImportIssueCodes.DuplicateFile, Path.GetFileName(shp),
                $"Same content as the {definition.Name} layer of region '{firstRegion}'; this copy is ignored.");
            return layer with { Verdict = LayerVerdict.Unusable };
        }

        firstRegionByHash.TryAdd(hash, region);
        if (ShapeFileProbe.FindGeometryDefect(shp) is { } defect)
        {
            Report(IssueSeverity.Error, ImportIssueCodes.CorruptGeometry, Path.GetFileName(shp), $"Corrupt geometry: {defect}.");
            return layer with { Verdict = LayerVerdict.Unusable };
        }

        return layer with { Verdict = LayerVerdict.Usable };
    }

    private async Task<LayerFileSet> PairAttributesAsync(
        LayerDefinition definition, string stem, LayerFileSet layer, string? orphan, CancellationToken cancellationToken)
    {
        var table = layer.AttributeTablePath;
        if (table is null && orphan is not null)
        {
            table = orphan;
            Report(IssueSeverity.Info, ImportIssueCodes.OrphanAttributeTable, Path.GetFileName(orphan),
                FormattableString.Invariant($"Paired with the {definition.Name} geometry by record count ({layer.RecordCount})."));
            if (string.Equals(layer.Encoding, ShapefileEncodings.Default, StringComparison.Ordinal) && Find(files, Path.GetFileNameWithoutExtension(orphan) + ".cpg", byName: true) is { } orphanCpg)
                layer = layer with { Encoding = await ReadEncodingAsync(orphanCpg, cancellationToken).ConfigureAwait(false) };
        }

        if (table is null)
        {
            Report(IssueSeverity.Warning, ImportIssueCodes.MissingSidecar, stem + ".dbf",
                "The layer has no attribute table; only its geometry can be imported.");
            return layer with { Verdict = LayerVerdict.GeometryOnly, FieldAliases = Fallback(definition) };
        }

        var aliases = await ReadAliasesAsync(definition, layer.GeometryPath!, table, cancellationToken).ConfigureAwait(false);
        return layer with { AttributeTablePath = table, Verdict = LayerVerdict.Usable, FieldAliases = aliases };
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadAliasesAsync(
        LayerDefinition definition, string shp, string table, CancellationToken cancellationToken)
    {
        var fieldNames = DbaseHeaderInfo.Read(table).FieldNames;
        var metadata = Find(files, Path.GetFileName(shp) + ".xml", byName: true);
        if (metadata is not null)
        {
            var fullNames = await ArcGisLineageAliases.ReadFullNamesAsync(metadata, cancellationToken).ConfigureAwait(false);
            if (fullNames.Count > 0 && fullNames.Count == fieldNames.Count)
            {
                var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < fieldNames.Count; i++)
                    aliases.TryAdd(fieldNames[i], fullNames[i]);
                return aliases;
            }
        }

        return Fallback(definition);
    }

    private Dictionary<string, string> Fallback(LayerDefinition definition) =>
        new(profile.FallbackFieldAliases(definition.Name), StringComparer.OrdinalIgnoreCase);

    private string? FindOrphanTable(long recordCount, string? stemPrefix) =>
        files
            .Where(file => string.Equals(Path.GetExtension(file), ".dbf", StringComparison.OrdinalIgnoreCase)
                && !_pairedOrphans.Contains(file)
                && (stemPrefix is null || Path.GetFileName(file).StartsWith(stemPrefix, StringComparison.OrdinalIgnoreCase))
                && !profile.Layers.Any(layer => LayerStemMatcher.IsKnown(layer, Path.GetFileNameWithoutExtension(file))))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault(file => DbaseHeaderInfo.Read(file).RecordCount == recordCount);

    private static long CountRecords(string shp, string? shx, string? dbf)
    {
        if (shx is not null)
            return Math.Max(0, (new FileInfo(shx).Length - 100) / 8);
        return dbf is not null ? DbaseHeaderInfo.Read(dbf).RecordCount : ShapeFileProbe.CountRecords(shp);
    }

    private static async Task<byte[]> HashAsync(string path, CancellationToken cancellationToken)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await using (stream.ConfigureAwait(false))
            return await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadEncodingAsync(string? cpg, CancellationToken cancellationToken)
    {
        if (cpg is null)
            return ShapefileEncodings.Default;
        var text = (await File.ReadAllTextAsync(cpg, cancellationToken).ConfigureAwait(false)).Trim();
        return text.Length == 0 ? ShapefileEncodings.Default : text;
    }

    private static string? Find(IEnumerable<string> candidates, string extensionOrName, bool byName = false) =>
        candidates.FirstOrDefault(file => string.Equals(
            byName ? Path.GetFileName(file) : Path.GetExtension(file), extensionOrName, StringComparison.OrdinalIgnoreCase));

    private void Report(IssueSeverity severity, string code, string file, string message)
    {
        var subject = region.Length == 0 ? file : $"{region}/{file}";
        issues.Add(new ImportIssue(severity, code, subject, message));
    }
}
