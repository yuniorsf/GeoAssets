using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Services;

/// <summary>
/// The <see cref="IMapSelectionResolver"/> a host gets when it registers no adapter of its own
/// (XD01-310): every area gesture fails with <see cref="NotSupportedException"/>, which the
/// selection coordinator shows as "not available here". Click selection needs no resolver and
/// keeps working.
/// </summary>
public sealed class UnavailableMapSelectionResolver : IMapSelectionResolver
{
    public Task<IReadOnlyList<string>> ResolveAsync(MapSelectionGesture gesture, CancellationToken ct) =>
        Task.FromException<IReadOnlyList<string>>(
            new NotSupportedException("This host has no map selection resolver registered."));
}
