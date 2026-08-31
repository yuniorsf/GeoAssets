namespace GeoAssets.Core.Navigation;

/// <summary>
/// Singleton that collects every <see cref="MenuItemBase"/> registered via
/// <see cref="MenuRegistrationExtensions.AddGeoAssetsNavigation"/> — mirrors the role
/// <c>GeoAssets.Core.Services.ProviderPluginRegistry</c> plays for <c>IProviderPlugin</c>.
/// </summary>
public sealed class MenuRegistry
{
    public IReadOnlyList<MenuItemBase> All { get; }

    public MenuRegistry(IEnumerable<MenuItemBase> items)
    {
        All = [.. items];

        // Checked here, not at registration time — an item's Id is an instance property, and
        // items may have constructor-injected dependencies (XD01-81), so DI has to build the
        // real instances before any Id is known. This still fails fast at startup, as long as
        // something resolves MenuRegistry eagerly (the app-wiring side of that is XD01-85).
        var duplicateIds = All
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateIds.Count > 0)
            throw new InvalidOperationException(
                $"Duplicate MenuItemBase.Id(s) found: {string.Join(", ", duplicateIds)}. " +
                "Each menu item's Id must be unique across the whole menu tree.");
    }
}
