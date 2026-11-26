using System.Xml;
using System.Xml.Linq;

namespace GeoAssets.Ingestion.Shapefile;

/// <summary>
/// Extracts the full field names of a layer from its ArcGIS metadata (<c>&lt;stem&gt;.shp.xml</c>): the field map of
/// the last lineage <c>Process</c> run by <c>FeatureClassToFeatureClass</c> — the text between <c>.shp # "</c> and
/// <c>" #</c>, one <c>FULLNAME "Alias" …</c> entry per output field, separated by <c>;</c>. The entries follow the
/// output table's field order, so they zip positionally with the <c>.dbf</c>'s (truncated) field names.
/// </summary>
internal static class ArcGisLineageAliases
{
    private const string ToolName = "FeatureClassToFeatureClass";
    private const string MapStart = ".shp # \"";
    private const string MapEnd = "\" #";

    public static async Task<IReadOnlyList<string>> ReadFullNamesAsync(string metadataPath, CancellationToken cancellationToken)
    {
        try
        {
            var stream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            await using (stream.ConfigureAwait(false))
            {
                var settings = new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
                using var reader = XmlReader.Create(stream, settings);
                var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken).ConfigureAwait(false);
                var process = document.Descendants()
                    .LastOrDefault(element => string.Equals(element.Name.LocalName, "Process", StringComparison.Ordinal)
                        && ((string?)element.Attribute("ToolSource"))?.Contains(ToolName, StringComparison.Ordinal) == true);
                return process is null ? [] : ParseFieldMap(process.Value);
            }
        }
        catch (XmlException)
        {
            return [];
        }
    }

    internal static IReadOnlyList<string> ParseFieldMap(string commandLine)
    {
        var start = commandLine.IndexOf(MapStart, StringComparison.Ordinal);
        if (start < 0)
            return [];
        start += MapStart.Length;
        var end = commandLine.LastIndexOf(MapEnd, StringComparison.Ordinal);
        if (end < start)
            end = commandLine.Length;

        var names = new List<string>();
        foreach (var entry in commandLine[start..end].Split(';'))
        {
            var trimmed = entry.Trim();
            var separator = trimmed.IndexOfAny([' ', '"', '\t']);
            var name = (separator < 0 ? trimmed : trimmed[..separator]).Trim('\\', '"');
            if (name.Length > 0)
                names.Add(name);
        }

        return names;
    }
}
