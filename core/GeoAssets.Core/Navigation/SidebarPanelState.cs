namespace GeoAssets.Core.Navigation;

/// <summary>
/// Tracks which <see cref="MenuPanelItem"/> is expanded in <c>NavMenu</c> (XD01-85) as shared,
/// circuit-scoped state — added by XD01-146 so <c>TopBar</c>'s quick-access Project menu can
/// reveal the Projects panel without duplicating <c>ProjectPanel</c>'s own project list and
/// switch logic.
/// </summary>
public sealed class SidebarPanelState
{
    private string? _openPanelId;

    public string? OpenPanelId
    {
        get => _openPanelId;
        set
        {
            if (_openPanelId == value) return;
            _openPanelId = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? Changed;

    /// <summary>Opens <paramref name="panelId"/> (collapsing whichever panel was open before).</summary>
    public void Open(string panelId) => OpenPanelId = panelId;

    /// <summary>Same toggle-or-collapse behavior <c>NavMenu</c>'s own panel-header click had.</summary>
    public void Toggle(string panelId) => OpenPanelId = OpenPanelId == panelId ? null : panelId;
}
