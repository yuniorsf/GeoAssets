namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// An <see cref="ICrossOrgEntitlementsService"/> for a host with no source of entitlements (the MAUI app until it gets
/// one): always <see cref="CrossOrgEntitlementsDto.None"/>, so its views behave as they did before XD01-329.
/// </summary>
public sealed class NoCrossOrgEntitlementsService : ICrossOrgEntitlementsService
{
    public Task<CrossOrgEntitlementsDto> GetAsync(CancellationToken ct = default) => Task.FromResult(CrossOrgEntitlementsDto.None);

    public Task<CrossOrgEntitlementsDto> RefreshAsync(CancellationToken ct = default) => Task.FromResult(CrossOrgEntitlementsDto.None);
}
