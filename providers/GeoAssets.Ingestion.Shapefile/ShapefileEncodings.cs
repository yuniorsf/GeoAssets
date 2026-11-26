using System.Globalization;
using System.Text;

namespace GeoAssets.Ingestion.Shapefile;

/// <summary>
/// Resolves a <c>.cpg</c> code-page declaration (<c>UTF-8</c>, <c>ISO-8859-1</c>, <c>1252</c>, <c>ANSI 1252</c>…) to an
/// <see cref="Encoding"/>. Windows/OEM code pages need <see cref="CodePagesEncodingProvider"/>, registered on first use.
/// </summary>
internal static class ShapefileEncodings
{
    public const string Default = "ISO-8859-1";

    private static readonly Lazy<bool> CodePagesRegistered = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return true;
    });

    public static Encoding Resolve(string? declaration)
    {
        _ = CodePagesRegistered.Value;
        var name = string.IsNullOrWhiteSpace(declaration) ? Default : declaration.Trim();
        if (name.StartsWith("ANSI", StringComparison.OrdinalIgnoreCase) || name.StartsWith("OEM", StringComparison.OrdinalIgnoreCase))
            name = name[(name.IndexOf(' ', StringComparison.Ordinal) + 1)..].Trim();

        if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var codePage))
            return TryGet(() => Encoding.GetEncoding(codePage == 88591 ? 28591 : codePage));

        if (string.Equals(name, "UTF8", StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF8;

        return TryGet(() => Encoding.GetEncoding(name));
    }

    private static Encoding TryGet(Func<Encoding> resolve)
    {
        try
        {
            return resolve();
        }
        catch (ArgumentException)
        {
            return Encoding.Latin1;
        }
        catch (NotSupportedException)
        {
            return Encoding.Latin1;
        }
    }
}
