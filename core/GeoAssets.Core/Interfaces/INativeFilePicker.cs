namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Picks a ZIP archive via the host's native file picker, returning its content alongside a real
/// full filesystem path — unlike a browser <c>&lt;input type=file&gt;</c> (the default UI for a
/// <see cref="Models.ProviderFieldType.BinaryFile"/> field), which never exposes one, by browser
/// design. Registered only where the host has normal filesystem access (MAUI desktop/mobile), not
/// on Web, so callers resolve it optionally and fall back to the browser file input where it's
/// unavailable — same defensive-optional pattern as <see cref="IStorageService"/>'s per-host split.
/// </summary>
public interface INativeFilePicker
{
    /// <summary>Opens the native picker filtered to ZIP archives. Returns <c>null</c> if the user cancelled.</summary>
    Task<NativeFilePickResult?> PickZipArchiveAsync(CancellationToken ct = default);
}

/// <summary>Result of a successful <see cref="INativeFilePicker.PickZipArchiveAsync"/> call.</summary>
public sealed record NativeFilePickResult(byte[] Content, string FileName, string FullPath);
