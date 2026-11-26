using GeoAssets.Core.Interfaces;

namespace GeoAssets.MAUI.Services;

/// <summary>
/// INativeFilePicker implementation for MAUI (Android, iOS, Windows, macOS) — uses
/// Microsoft.Maui.Storage.FilePicker, whose FileResult exposes a real FullPath unlike a browser
/// file input, so the picked file can be reopened directly from disk on a later reconnect.
/// Windows and macOS filter by extension; iOS and Android filter by content type, so an extension
/// without a known one there (e.g. <c>.pmtiles</c>) offers any file.
/// </summary>
public sealed class MauiNativeFilePicker : INativeFilePicker
{
    private const string AnyAppleType = "public.data";
    private const string AnyAndroidType = "*/*";

    private static readonly Dictionary<string, string[]> AppleTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".zip"] = ["com.pkware.zip-archive"],
    };

    private static readonly Dictionary<string, string[]> AndroidTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".zip"] = ["application/zip", "application/x-zip-compressed"],
    };

    public async Task<NativeFilePickResult?> PickBinaryFileAsync(IReadOnlyList<string>? acceptedExtensions = null, CancellationToken ct = default)
    {
        var extensions = acceptedExtensions?.Where(e => !string.IsNullOrWhiteSpace(e)).ToArray() ?? [];
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = extensions.Length == 0 ? "Select a file" : $"Select a {string.Join(", ", extensions)} file",
            FileTypes = extensions.Length == 0 ? null : FileTypesFor(extensions)
        });
        if (result is null) return null;

        using var stream = await result.OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return new NativeFilePickResult(ms.ToArray(), result.FileName, result.FullPath);
    }

    private static FilePickerFileType FileTypesFor(string[] extensions) =>
        new(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.iOS,     [.. ContentTypesFor(extensions, AppleTypes, AnyAppleType, alwaysIncludeFallback: true)] },
            { DevicePlatform.Android, [.. ContentTypesFor(extensions, AndroidTypes, AnyAndroidType, alwaysIncludeFallback: false)] },
            { DevicePlatform.WinUI,   extensions },
            { DevicePlatform.macOS,   [.. extensions.Select(e => e.TrimStart('.'))] }
        });

    private static IEnumerable<string> ContentTypesFor(
        string[] extensions, Dictionary<string, string[]> known, string fallback, bool alwaysIncludeFallback)
    {
        var types = new List<string>();
        var anyUnknown = false;
        foreach (var extension in extensions)
        {
            if (known.TryGetValue(extension, out var mapped)) types.AddRange(mapped);
            else anyUnknown = true;
        }

        if (anyUnknown || alwaysIncludeFallback) types.Add(fallback);
        return types.Distinct(StringComparer.Ordinal);
    }
}
