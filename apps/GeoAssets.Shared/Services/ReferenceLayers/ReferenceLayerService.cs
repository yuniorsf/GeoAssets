using GeoAssets.Core.Rendering;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Services.ReferenceLayers;

/// <summary>
/// Another organization's assets as a labelled, non-interactive reference layer on the map (XD01-331): which organizations
/// to offer (<see cref="ReferenceLayerOffer"/>), which the user switched on (off by default, kept for the session), and
/// keeping each switched-on layer in step with the map's viewport through <see cref="IReferenceFeatureClient"/> and
/// <see cref="IMapInterop.SetReferenceLayerAsync"/>.
///
/// <para>Reference features never touch <see cref="GeoAssets.Core.Interfaces.IAssetProvider"/>: this service holds no
/// provider, so they never reach the active provider, the feature list, auto-save, selection, export or the project
/// session — they exist only as JSON handed to the map.</para>
///
/// <para>A viewport change (<see cref="OnViewportChangedAsync"/>) at once supersedes every load in flight and, after
/// <see cref="DebounceInterval"/> without a newer change, loads each switched-on layer for the new view, replacing the
/// old area. A <c>403</c> — the entitlement was revoked mid-session — clears that layer and withdraws its toggle for the
/// rest of the session, without an error; any other failure is logged and leaves the layer as it was.</para>
///
/// <para>Scoped (the session, in Blazor WebAssembly), so the toggles outlive the layers panel. One map at a time: the one
/// last <see cref="AttachAsync">attached</see>. Not thread-safe — renderer context only.</para>
/// </summary>
public sealed class ReferenceLayerService(
    IMapInterop map,
    IReferenceFeatureClient client,
    ICrossOrgEntitlementsService entitlements,
    IGeoAuthorizationService authorization,
    TimeProvider timeProvider,
    ILogger<ReferenceLayerService> logger) : IDisposable
{
    /// <summary>How long the viewport must stay put before the layers are reloaded for it.</summary>
    public static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(300);

    /// <summary>How the layers are drawn: labels from zoom 16 (street level) up.</summary>
    public static readonly ReferenceLayerOptions LayerOptions = new(LabelMinZoom: 16);

    private readonly HashSet<Guid> _enabled = [];
    private readonly HashSet<Guid> _revoked = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _loads = [];
    private IReadOnlyList<ReferenceOrganization> _organizations = [];
    private CancellationTokenSource? _debounce;
    private string? _divId;
    private BoundingBox? _viewport;

    /// <summary>The offered organizations changed (one was withdrawn): toggles should re-read <see cref="Organizations"/>.</summary>
    public event EventHandler? Changed;

    /// <summary>The organizations offered, as last loaded by <see cref="LoadOrganizationsAsync"/>, minus any withdrawn since.</summary>
    public IReadOnlyList<ReferenceOrganization> Organizations => _organizations;

    /// <summary>The map layer id of <paramref name="organizationId"/>'s reference layer.</summary>
    public static string LayerIdFor(Guid organizationId) => $"reference-{organizationId:N}";

    /// <summary>Whether the user has <paramref name="organizationId"/>'s layer switched on.</summary>
    public bool IsEnabled(Guid organizationId) => _enabled.Contains(organizationId);

    /// <summary>
    /// Works out the organizations to offer from the caller's current entitlements and home context. A caller whose
    /// entitlements hold no candidate grant is answered without reading the home context. An organization withdrawn by a
    /// <c>403</c> stays withdrawn; one no longer offered is switched off. Never throws for a failed lookup: it offers none.
    /// </summary>
    public async Task<IReadOnlyList<ReferenceOrganization>> LoadOrganizationsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ReferenceOrganization> offered;
        try
        {
            offered = await ResolveOfferAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not work out which organizations' reference layers to offer — offering none");
            offered = [];
        }

        _organizations = [.. offered.Where(o => !_revoked.Contains(o.Id))];

        foreach (var gone in _enabled.Where(id => _organizations.All(o => o.Id != id)).ToList())
            await DisableAsync(gone);

        return _organizations;
    }

    /// <summary>
    /// Switches <paramref name="organizationId"/>'s layer on — loading it at once for the current view of the attached
    /// map — or off, clearing it. Switching on an organization that is not offered does nothing.
    /// </summary>
    public Task SetEnabledAsync(Guid organizationId, bool enabled) =>
        enabled ? EnableAsync(organizationId) : DisableAsync(organizationId);

    /// <summary>
    /// The map <paramref name="divId"/> is ready (its handlers registered): layers switched on before are drawn on it for
    /// its current view.
    /// </summary>
    public async Task AttachAsync(string divId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(divId);
        CancelPending();
        _divId = divId;
        _viewport = null;

        if (_enabled.Count == 0 || await ReadViewportAsync(divId) is not { } bbox) return;
        await Task.WhenAll(_enabled.ToList().Select(id => LoadAsync(divId, id, bbox)));
    }

    /// <summary>The map <paramref name="divId"/> is being destroyed: its loads are cancelled and it is forgotten.</summary>
    public void Detach(string divId)
    {
        if (!string.Equals(_divId, divId, StringComparison.Ordinal)) return;
        CancelPending();
        _divId = null;
        _viewport = null;
    }

    /// <summary>
    /// The attached map's view settled on these bounds (<c>OnViewportChangedFromJs</c>). Completes once the debounced
    /// loads it started are done, or at once when a newer change supersedes it.
    /// </summary>
    public async Task OnViewportChangedAsync(string divId, double minLon, double minLat, double maxLon, double maxLat)
    {
        if (!string.Equals(_divId, divId, StringComparison.Ordinal)) return;

        CancelPending();
        _viewport = ReferenceViewport.Normalize(minLon, minLat, maxLon, maxLat);
        if (_enabled.Count == 0 || _viewport is not { } bbox) return;

        var debounce = new CancellationTokenSource();
        _debounce = debounce;
        var ct = debounce.Token;
        try
        {
            await Task.Delay(DebounceInterval, timeProvider, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await Task.WhenAll(_enabled.ToList().Select(id => LoadAsync(divId, id, bbox)));
    }

    public void Dispose()
    {
        foreach (var id in _loads.Keys.ToList()) CancelLoad(id);
        _debounce?.Cancel();
        _debounce?.Dispose();
        _debounce = null;
    }

    private async Task<IReadOnlyList<ReferenceOrganization>> ResolveOfferAsync(CancellationToken ct)
    {
        var current = await entitlements.GetAsync(ct);
        if (!ReferenceLayerOffer.AnyCandidate(current)) return [];

        var home = await authorization.GetAuthorizationContextAsync(ct);
        return ReferenceLayerOffer.Organizations(home, current, timeProvider.GetUtcNow().UtcDateTime);
    }

    private async Task EnableAsync(Guid organizationId)
    {
        if (_organizations.All(o => o.Id != organizationId) || !_enabled.Add(organizationId)) return;
        if (_divId is not { } divId) return;

        var bbox = _viewport ?? await ReadViewportAsync(divId);
        if (bbox is { } viewport) await LoadAsync(divId, organizationId, viewport);
    }

    private async Task DisableAsync(Guid organizationId)
    {
        if (!_enabled.Remove(organizationId)) return;
        CancelLoad(organizationId);
        if (_divId is { } divId) await ClearAsync(divId, organizationId);
    }

    /// <summary>Loads one layer for <paramref name="bbox"/>, superseding that layer's load in flight.</summary>
    private async Task LoadAsync(string divId, Guid organizationId, BoundingBox bbox)
    {
        CancelLoad(organizationId);
        var load = new CancellationTokenSource();
        _loads[organizationId] = load;
        var ct = load.Token;

        ReferenceFeatureResponse response;
        try
        {
            response = await client.GetAsync(organizationId, bbox, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load the reference layer of organization {OrganizationId}", organizationId);
            return;
        }

        if (ct.IsCancellationRequested || !IsCurrent(divId, organizationId)) return;

        if (response.IsForbidden)
        {
            await WithdrawAsync(divId, organizationId);
            return;
        }

        try
        {
            await map.SetReferenceLayerAsync(divId, LayerIdFor(organizationId), response.FeaturesJson, LayerOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not draw the reference layer of organization {OrganizationId} on map {DivId}", organizationId, divId);
        }
    }

    /// <summary>The server no longer opens the organization: clear its layer and withdraw its toggle, silently.</summary>
    private async Task WithdrawAsync(string divId, Guid organizationId)
    {
        logger.LogInformation(
            "The server refused the reference layer of organization {OrganizationId} — withdrawing it for this session", organizationId);

        _revoked.Add(organizationId);
        _enabled.Remove(organizationId);
        _organizations = [.. _organizations.Where(o => o.Id != organizationId)];
        await ClearAsync(divId, organizationId);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task ClearAsync(string divId, Guid organizationId)
    {
        try
        {
            await map.ClearReferenceLayerAsync(divId, LayerIdFor(organizationId));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not clear the reference layer of organization {OrganizationId} on map {DivId}", organizationId, divId);
        }
    }

    private async Task<BoundingBox?> ReadViewportAsync(string divId)
    {
        try
        {
            var bounds = await map.GetViewBoundsAsync(divId);
            _viewport = bounds is { } b ? ReferenceViewport.Normalize(b.MinLon, b.MinLat, b.MaxLon, b.MaxLat) : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read the view of map {DivId}", divId);
            _viewport = null;
        }

        return _viewport;
    }

    private bool IsCurrent(string divId, Guid organizationId) =>
        _enabled.Contains(organizationId) && string.Equals(_divId, divId, StringComparison.Ordinal);

    /// <summary>
    /// Supersedes the pending debounce and every load in flight. A cancelled source may be disposed at once: its token
    /// still reports the cancellation, and a callback registered on it later runs immediately.
    /// </summary>
    private void CancelPending()
    {
        if (_debounce is { } debounce)
        {
            _debounce = null;
            debounce.Cancel();
            debounce.Dispose();
        }

        foreach (var id in _loads.Keys.ToList()) CancelLoad(id);
    }

    private void CancelLoad(Guid organizationId)
    {
        if (!_loads.Remove(organizationId, out var load)) return;
        load.Cancel();
        load.Dispose();
    }
}
