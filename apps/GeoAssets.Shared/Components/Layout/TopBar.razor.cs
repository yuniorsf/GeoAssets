using GeoAssets.Core.Theming;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Layout;

public partial class TopBar
{
    [Parameter] public string UserDisplayName { get; set; } = string.Empty;
    [Parameter] public string? OrganizationName { get; set; }
    [Parameter] public IReadOnlyList<string> Roles { get; set; } = [];

    [Parameter] public EventCallback OnSignOut { get; set; }

    private bool _userMenuOpen;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => InvokeAsync(StateHasChanged);

    private async Task SetTheme(ThemeMode mode) => await ThemeService.SetModeAsync(mode);

    private void ToggleUserMenu() => _userMenuOpen = !_userMenuOpen;
    private void CloseUserMenu()  => _userMenuOpen = false;

    private async Task HandleSignOut()
    {
        CloseUserMenu();
        await OnSignOut.InvokeAsync();
    }

    public override void Dispose()
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
        base.Dispose();
    }
}
