namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// Cross-cutting context identifying which map div id self-sufficient components should target —
/// replaces the bespoke <c>MapDivId</c> parameter threaded through <c>AssetTypeManager</c> and
/// <c>ProviderPoolPanel</c> from <c>Index.razor</c>'s own <c>_mapDivId</c> constant (XD01-84).
/// </summary>
public interface ICurrentMapContext
{
    string MapDivId { get; }
}
