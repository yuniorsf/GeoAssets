namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Picks a file via the host's native file picker, returning its content alongside a real full
/// filesystem path — unlike a browser <c>&lt;input type=file&gt;</c> (the default UI for a
/// <see cref="Models.ProviderFieldType.BinaryFile"/> field), which never exposes one, by browser
/// design. Registered only where the host has normal filesystem access (MAUI desktop/mobile), not
/// on Web, so callers resolve it optionally and fall back to the browser file input where it's
/// unavailable — same defensive-optional pattern as <see cref="IStorageService"/>'s per-host split.
/// </summary>
public interface INativeFilePicker
{
    /// <summary>
    /// Opens the native picker filtered to <paramref name="acceptedExtensions"/>. Returns <c>null</c>
    /// if the user cancelled.
    /// </summary>
    /// <param name="acceptedExtensions">
    /// The file extensions to offer, each with its leading dot (e.g. <c>.zip</c>, <c>.pmtiles</c>) —
    /// what <see cref="Models.ProviderConfigField.GetAcceptedExtensions"/> returns for a field. <c>null</c>
    /// or empty offers any file. A platform whose picker filters by content type rather than by
    /// extension offers the closest type it has, so callers must not assume the picked file has one
    /// of these extensions.
    /// </param>
    /// <param name="ct">Cancels reading the picked file's content.</param>
    Task<NativeFilePickResult?> PickBinaryFileAsync(IReadOnlyList<string>? acceptedExtensions = null, CancellationToken ct = default);
}
