using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Shared.Tests.Services.ReferenceLayers;

/// <summary>The caller's entitlements, as <see cref="ICrossOrgEntitlementsService"/> answers them; counts its reads.</summary>
internal sealed class FakeEntitlements(CrossOrgEntitlementsDto current) : ICrossOrgEntitlementsService
{
    public CrossOrgEntitlementsDto Current { get; set; } = current;
    public Exception? FailWith { get; set; }
    public int Reads { get; private set; }

    public Task<CrossOrgEntitlementsDto> GetAsync(CancellationToken ct = default)
    {
        Reads++;
        return FailWith is { } failure ? Task.FromException<CrossOrgEntitlementsDto>(failure) : Task.FromResult(Current);
    }

    public Task<CrossOrgEntitlementsDto> RefreshAsync(CancellationToken ct = default) => GetAsync(ct);
}
