using GeoAssets.Core.Interfaces;

namespace GeoAssets.MAUI.Services;

/// <summary>
/// INativeFilePicker implementation for MAUI (Android, iOS, Mac Catalyst, Windows) — uses
/// Microsoft.Maui.Storage.FilePicker, whose FileResult exposes a real FullPath unlike a browser
/// file input, so the picked file can be reopened directly from disk on a later reconnect.
/// Each platform's filter for the accepted extensions is <see cref="NativeFilePickerFileTypes"/>'s —
/// including Mac Catalyst (XD01-438), which the system picker looks up by its own platform and which
/// filters by Uniform Type Identifier like iOS. <see cref="NativeFilePickContent.PathOnly"/> answers the
/// picked file's path without opening it, so a multi-gigabyte offline map pack is never read into memory.
/// </summary>
internal sealed class MauiNativeFilePicker : INativeFilePicker
{
    public async Task<NativeFilePickResult?> PickBinaryFileAsync(
        IReadOnlyList<string>? acceptedExtensions = null,
        NativeFilePickContent content = NativeFilePickContent.Bytes,
        CancellationToken ct = default)
    {
        var extensions = acceptedExtensions?.Where(e => !string.IsNullOrWhiteSpace(e)).ToArray() ?? [];
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = extensions.Length == 0 ? "Select a file" : $"Select a {string.Join(", ", extensions)} file",
            FileTypes = extensions.Length == 0 ? null : FileTypesFor(extensions)
        });
        if (result is null) return null;
        if (content == NativeFilePickContent.PathOnly) return new NativeFilePickResult([], result.FileName, result.FullPath);

        using var stream = await result.OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return new NativeFilePickResult(ms.ToArray(), result.FileName, result.FullPath);
    }

    private static FilePickerFileType FileTypesFor(string[] extensions) =>
        new(NativeFilePickerFileTypes.For(extensions).ToDictionary(
            kv => DevicePlatformOf(kv.Key),
            kv => (IEnumerable<string>)kv.Value));

    private static DevicePlatform DevicePlatformOf(NativeFilePickerPlatform platform) => platform switch
    {
        NativeFilePickerPlatform.IOS => DevicePlatform.iOS,
        NativeFilePickerPlatform.MacCatalyst => DevicePlatform.MacCatalyst,
        NativeFilePickerPlatform.Android => DevicePlatform.Android,
        NativeFilePickerPlatform.WinUI => DevicePlatform.WinUI,
        NativeFilePickerPlatform.MacOS => DevicePlatform.macOS,
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unknown file picker platform."),
    };
}
