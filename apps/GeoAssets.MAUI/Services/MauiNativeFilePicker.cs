using GeoAssets.Core.Interfaces;

namespace GeoAssets.MAUI.Services;

/// <summary>
/// INativeFilePicker implementation for MAUI (Android, iOS, Windows, macOS) — uses
/// Microsoft.Maui.Storage.FilePicker, whose FileResult exposes a real FullPath unlike a browser
/// file input, so the picked archive can be reopened directly from disk on a later reconnect.
/// </summary>
public sealed class MauiNativeFilePicker : INativeFilePicker
{
    public async Task<NativeFilePickResult?> PickZipArchiveAsync(CancellationToken ct = default)
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select Shapefile ZIP archive",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.iOS,     ["com.pkware.zip-archive", "public.data"] },
                { DevicePlatform.Android, ["application/zip", "application/x-zip-compressed"] },
                { DevicePlatform.WinUI,   [".zip"] },
                { DevicePlatform.macOS,   ["zip"] }
            })
        });
        if (result is null) return null;

        using var stream = await result.OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return new NativeFilePickResult(ms.ToArray(), result.FileName, result.FullPath);
    }
}
