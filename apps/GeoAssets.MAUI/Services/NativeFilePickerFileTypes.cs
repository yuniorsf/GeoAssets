namespace GeoAssets.MAUI.Services;

/// <summary>
/// What <see cref="MauiNativeFilePicker"/> asks each platform's picker to offer for a set of file extensions (XD01-438)
/// — plain .NET, no MAUI type, so it is unit-tested outside a device. Windows and AppKit macOS filter by extension
/// (macOS without the dot); iOS and Mac Catalyst filter by Uniform Type Identifier and Android by MIME type, so an
/// extension maps to the types it is known by there: <c>.zip</c> to its archive types, <c>.pmtiles</c> to
/// <c>application/vnd.pmtiles</c> and <c>application/octet-stream</c> on Android (what a document provider reports for a
/// file it has no type for). Apple has no system type for <c>.pmtiles</c>; the Apple platforms always also offer
/// <c>public.data</c> (any file), and Android offers <c>*/*</c> once an extension has no known type, so a file is never
/// unpickable — the caller still checks what was picked.
/// </summary>
internal static class NativeFilePickerFileTypes
{
    public const string AnyAppleType = "public.data";
    public const string AnyAndroidType = "*/*";

    private static readonly Dictionary<string, string[]> AppleTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".zip"] = ["com.pkware.zip-archive"],
    };

    private static readonly Dictionary<string, string[]> AndroidTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".zip"] = ["application/zip", "application/x-zip-compressed"],
        [".pmtiles"] = ["application/vnd.pmtiles", "application/octet-stream"],
    };

    /// <summary>The filter of each platform for <paramref name="extensions"/> (each with its leading dot).</summary>
    public static IReadOnlyDictionary<NativeFilePickerPlatform, IReadOnlyList<string>> For(IReadOnlyList<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        var apple = ContentTypesFor(extensions, AppleTypes, AnyAppleType, alwaysIncludeFallback: true);
        return new Dictionary<NativeFilePickerPlatform, IReadOnlyList<string>>
        {
            [NativeFilePickerPlatform.IOS] = apple,
            [NativeFilePickerPlatform.MacCatalyst] = apple,
            [NativeFilePickerPlatform.Android] = ContentTypesFor(extensions, AndroidTypes, AnyAndroidType, alwaysIncludeFallback: false),
            [NativeFilePickerPlatform.WinUI] = [.. extensions],
            [NativeFilePickerPlatform.MacOS] = [.. extensions.Select(e => e.TrimStart('.'))],
        };
    }

    private static string[] ContentTypesFor(
        IReadOnlyList<string> extensions, Dictionary<string, string[]> known, string fallback, bool alwaysIncludeFallback)
    {
        var types = new List<string>();
        var anyUnknown = false;
        foreach (var extension in extensions)
        {
            if (known.TryGetValue(extension, out var mapped)) types.AddRange(mapped);
            else anyUnknown = true;
        }

        if (anyUnknown || alwaysIncludeFallback) types.Add(fallback);
        return [.. types.Distinct(StringComparer.Ordinal)];
    }
}
