using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Services;

/// <summary>
/// Default <see cref="IProjectSessionService"/> — see that interface for the full contract
/// (XD01-142).
///
/// <b>Known gap, deliberately out of scope here</b>: <see cref="IProviderPool.ProviderEntry"/>
/// doesn't retain the <c>PluginId</c>/reconnect config values it was created from (only
/// <c>ProjectProviderEntry</c>, the persisted shape, does) — so there is currently no reliable
/// way to snapshot the live pool's actual state (renames, open/close/enable/disable, or newly
/// connected entries) back into a <see cref="Project.Providers"/> list to persist. Dirty
/// tracking still reacts to <see cref="IProviderPool.Changed"/> (so the UI/close-guard correctly
/// sees unsaved pool changes), but <see cref="SaveAsync"/> does not attempt to round-trip them —
/// doing so would require extending <c>ProviderEntry</c> with that metadata first, a separate,
/// substantial follow-up.
/// </summary>
public sealed class BlazorProjectSessionService : IProjectSessionService, IDisposable
{
    private static readonly JsonSerializerOptions DiffOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IProjectClient _client;
    private readonly IProviderPool _pool;
    private readonly ProviderPluginRegistry _registry;
    private readonly IServiceProvider _services;
    private readonly IMapInterop _mapInterop;
    private readonly ICurrentMapContext _mapContext;
    private readonly ILogger<BlazorProjectSessionService> _logger;

    private Project? _rawBaseline;
    private Project? _liveRaw;
    private Project? _parent;

    public BlazorProjectSessionService(
        IProjectClient client, IProviderPool pool, ProviderPluginRegistry registry,
        IServiceProvider services, IMapInterop mapInterop, ICurrentMapContext mapContext,
        ILogger<BlazorProjectSessionService> logger)
    {
        _client     = client;
        _pool       = pool;
        _registry   = registry;
        _services   = services;
        _mapInterop = mapInterop;
        _mapContext = mapContext;
        _logger     = logger;
    }

    public Project? Current { get; private set; }
    public bool IsDirty { get; private set; }
    public event EventHandler? DirtyChanged;
    public event EventHandler<Project>? Saved;
    public event EventHandler? CurrentChanged;
    public Func<Task<ProjectCloseChoice>>? CloseRequested { get; set; }

    // ── OpenAsync ─────────────────────────────────────────────────────────────

    public async Task OpenAsync(Guid projectId, CancellationToken ct = default)
    {
        DetachPoolTracking();

        var raw = await _client.GetByIdAsync(projectId, ct)
            ?? throw new KeyNotFoundException($"Project '{projectId}' not found.");

        var (resolved, parent) = await ResolveAsync(raw, ct);

        // _rawBaseline, _liveRaw, and Current must be three independent instances — for a
        // Kind == General Project, "resolved" is otherwise the very same object as "raw",
        // which would silently corrupt the baseline the moment a scope setter mutates Current.
        _rawBaseline = CloneRaw(raw);
        _liveRaw     = CloneRaw(raw);
        _parent      = parent;
        Current      = raw.Kind == ProjectKind.User ? resolved : CloneRaw(raw);

        _pool.ClearAll();
        await ReconnectProvidersAsync(resolved.Providers, ct);
        await ApplyViewStateAsync(resolved.ViewState, ct);

        AttachPoolTracking();
        SetDirty(false);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<(Project Resolved, Project? Parent)> ResolveAsync(Project raw, CancellationToken ct)
    {
        if (raw.Kind != ProjectKind.User)
            return (raw, null);

        var parentId = raw.ParentProjectId
            ?? throw new InvalidOperationException($"User Project '{raw.Id}' has no ParentProjectId.");
        var parent = await _client.GetByIdAsync(parentId, ct)
            ?? throw new KeyNotFoundException($"Parent Project '{parentId}' not found.");

        return (ProjectResolver.Resolve(raw, parent), parent);
    }

    private async Task ReconnectProvidersAsync(List<ProjectProviderEntry>? providers, CancellationToken ct)
    {
        if (providers is null) return;

        foreach (var entry in providers.OrderBy(p => p.Position))
        {
            try
            {
                var plugin = _registry.Find(entry.PluginId);
                if (plugin is null)
                {
                    _logger.LogWarning(
                        "Reconnect skipped — unknown plugin '{PluginId}' for provider '{Name}'",
                        entry.PluginId, entry.Name);
                    continue;
                }

                var config = new ProviderConfig(entry.Values);
                var provider = await plugin.CreateAsync(config, _services, ct);
                _pool.RestoreEntry(entry.Name, provider, entry.Position, entry.IsOpen, entry.IsEnabled, entry.IsActive);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Reconnect failed for provider '{Name}' (plugin '{PluginId}') — skipped",
                    entry.Name, entry.PluginId);
            }
        }
    }

    private Task ApplyViewStateAsync(ProjectViewState? view, CancellationToken ct) =>
        view is null ? Task.CompletedTask : _mapInterop.SetViewAsync(_mapContext.MapDivId, view.Lat, view.Lon, view.Zoom);

    // ── SaveAsync ─────────────────────────────────────────────────────────────

    public async Task SaveAsync(CancellationToken ct = default)
    {
        EnsureOpen();

        // Not the same object as Current below once a redirect happens mid-save — captured up
        // front so a fork created partway through still resolves against the right parent.
        var originalGeneral = Current!.Kind == ProjectKind.General ? Current : null;
        var targetId = Current.Id;
        var redirected = false;

        if (ScopeChanged(_liveRaw!.AssetTypeScope, _rawBaseline!.AssetTypeScope))
        {
            var result = await _client.UpdateAssetTypeScopeAsync(targetId, _liveRaw.AssetTypeScope, ct);
            (redirected, targetId) = Track(result, targetId, redirected);
        }
        if (ScopeChanged(_liveRaw.LayerScope, _rawBaseline.LayerScope))
        {
            var result = await _client.UpdateLayerScopeAsync(targetId, _liveRaw.LayerScope, ct);
            (redirected, targetId) = Track(result, targetId, redirected);
        }
        if (ScopeChanged(_liveRaw.ViewState, _rawBaseline.ViewState))
        {
            var result = await _client.UpdateViewStateAsync(targetId, _liveRaw.ViewState, ct);
            (redirected, targetId) = Track(result, targetId, redirected);
        }

        var newRaw = await _client.GetByIdAsync(targetId, ct)
            ?? throw new KeyNotFoundException($"Project '{targetId}' not found after save.");

        if (redirected && originalGeneral is not null)
            _parent = originalGeneral;

        // See OpenAsync's comment: _rawBaseline/_liveRaw/Current must stay three independent
        // instances, or a later scope setter mutating Current would corrupt the new baseline.
        _rawBaseline = CloneRaw(newRaw);
        _liveRaw     = CloneRaw(newRaw);
        Current = newRaw.Kind == ProjectKind.User
            ? ProjectResolver.Resolve(newRaw, _parent ?? throw new InvalidOperationException(
                "Resolved User Project has no known parent."))
            : CloneRaw(newRaw);

        SetDirty(false);
        Saved?.Invoke(this, Current);
        if (redirected) CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── SaveAsAsync ("Save As") ───────────────────────────────────────────────

    public async Task SaveAsAsync(string name, string description, CancellationToken ct = default)
    {
        EnsureOpen();

        var parentId = Current!.Kind == ProjectKind.General
            ? Current.Id
            : Current.ParentProjectId ?? throw new InvalidOperationException(
                "Current Project has no General ancestor to fork from.");

        // Raw (possibly-null) working-copy scopes, not the resolved view — same reasoning as
        // SaveAsync: a scope the caller never touched should keep inheriting from the new
        // fork's parent, not get baked in as a permanent override.
        var newProject = new Project
        {
            Name            = name,
            Description     = description,
            ParentProjectId = parentId,
            Providers       = _liveRaw!.Providers,
            AssetTypeScope  = _liveRaw.AssetTypeScope,
            LayerScope      = _liveRaw.LayerScope,
            ViewState       = _liveRaw.ViewState,
        };

        var created = await _client.CreateAsync(newProject, ct);
        await OpenAsync(created.Id, ct);
    }

    private static (bool Redirected, Guid TargetId) Track(Project result, Guid previousTargetId, bool alreadyRedirected) =>
        result.Id != previousTargetId ? (true, result.Id) : (alreadyRedirected, previousTargetId);

    private static bool ScopeChanged<T>(T? live, T? baseline) =>
        JsonSerializer.Serialize(live, DiffOptions) != JsonSerializer.Serialize(baseline, DiffOptions);

    // ── DiscardChangesAsync ───────────────────────────────────────────────────

    public async Task DiscardChangesAsync(CancellationToken ct = default)
    {
        EnsureOpen();
        DetachPoolTracking();

        _liveRaw = CloneRaw(_rawBaseline!);
        Current = _rawBaseline!.Kind == ProjectKind.User
            ? ProjectResolver.Resolve(_rawBaseline, _parent ?? throw new InvalidOperationException(
                "Resolved User Project has no known parent."))
            : CloneRaw(_rawBaseline);

        _pool.ClearAll();
        await ReconnectProvidersAsync(Current.Providers, ct);
        await ApplyViewStateAsync(Current.ViewState, ct);

        AttachPoolTracking();
        SetDirty(false);
    }

    // ── CloseAsync ────────────────────────────────────────────────────────────

    public Task CloseAsync(CancellationToken ct = default)
    {
        DetachPoolTracking();

        _rawBaseline = null;
        _liveRaw     = null;
        _parent      = null;
        Current      = null;

        _pool.ClearAll();
        SetDirty(false);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    // ── RequestCloseAsync ─────────────────────────────────────────────────────

    public async Task<bool> RequestCloseAsync()
    {
        if (!IsDirty) return true;
        if (CloseRequested is null) return false;

        return await CloseRequested.Invoke() switch
        {
            ProjectCloseChoice.SaveAndClose    => await SaveThenTrueAsync(),
            ProjectCloseChoice.DiscardAndClose => await DiscardThenTrueAsync(),
            _                                  => false,
        };
    }

    private async Task<bool> SaveThenTrueAsync() { await SaveAsync(); return true; }
    private async Task<bool> DiscardThenTrueAsync() { await DiscardChangesAsync(); return true; }

    // ── Scope setters ─────────────────────────────────────────────────────────

    public void SetAssetTypeScope(ProjectAssetTypeScope scope)
    {
        EnsureOpen();
        _liveRaw!.AssetTypeScope = scope;
        Current!.AssetTypeScope  = scope;
        SetDirty(true);
    }

    public void SetLayerScope(ProjectLayerScope scope)
    {
        EnsureOpen();
        _liveRaw!.LayerScope = scope;
        Current!.LayerScope  = scope;
        SetDirty(true);
    }

    public void SetViewState(ProjectViewState viewState)
    {
        EnsureOpen();
        _liveRaw!.ViewState = viewState;
        Current!.ViewState  = viewState;
        SetDirty(true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void EnsureOpen()
    {
        if (Current is null || _liveRaw is null || _rawBaseline is null)
            throw new InvalidOperationException("No Project is open.");
    }

    private void AttachPoolTracking() => _pool.Changed += OnPoolChanged;
    private void DetachPoolTracking() => _pool.Changed -= OnPoolChanged;
    private void OnPoolChanged(object? sender, EventArgs e) => SetDirty(true);

    private void SetDirty(bool value)
    {
        if (IsDirty == value) return;
        IsDirty = value;
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Project CloneRaw(Project source) => new()
    {
        Id              = source.Id,
        Name            = source.Name,
        Description     = source.Description,
        OrganizationId  = source.OrganizationId,
        CreatedByUserId = source.CreatedByUserId,
        CreatedAt       = source.CreatedAt,
        UpdatedAt       = source.UpdatedAt,
        SchemaVersion   = source.SchemaVersion,
        Kind            = source.Kind,
        ParentProjectId = source.ParentProjectId,
        Providers       = source.Providers is null ? null : [.. source.Providers],
        AssetTypeScope  = source.AssetTypeScope is null ? null : new ProjectAssetTypeScope
        {
            VisibleAssetTypeIds = [.. source.AssetTypeScope.VisibleAssetTypeIds]
        },
        LayerScope = source.LayerScope is null ? null : new ProjectLayerScope
        {
            Overrides = [.. source.LayerScope.Overrides]
        },
        ViewState = source.ViewState is null ? null : new ProjectViewState
        {
            Lat  = source.ViewState.Lat,
            Lon  = source.ViewState.Lon,
            Zoom = source.ViewState.Zoom
        },
    };

    public void Dispose() => DetachPoolTracking();
}
