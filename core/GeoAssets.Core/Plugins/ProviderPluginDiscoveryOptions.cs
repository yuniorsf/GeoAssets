using GeoAssets.Core.Interfaces;

namespace GeoAssets.Core.Plugins;

/// <summary>How <see cref="ProviderPluginRegistrationExtensions.AddDiscoveredProviderPlugins"/> picks the modules it registers.</summary>
public sealed class ProviderPluginDiscoveryOptions
{
    /// <summary>
    /// The host being composed — exactly one of <see cref="ProviderHostTargets.Server"/>,
    /// <see cref="ProviderHostTargets.Browser"/> or <see cref="ProviderHostTargets.MobileDesktop"/>; required. The host
    /// states it: discovery doesn't guess it from the runtime. A module whose
    /// <see cref="IProviderPluginModule.SupportedHosts"/> doesn't include it is skipped.
    /// </summary>
    public ProviderHostTargets HostTarget { get; set; }

    /// <summary>
    /// When not empty, the only module ids registered (case-insensitive); every other module is skipped. An id no
    /// source offers is ignored, so one allowlist can serve several discovery calls.
    /// </summary>
    public ISet<string> EnabledPluginIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Module ids never registered (case-insensitive); wins over <see cref="EnabledPluginIds"/>.</summary>
    public ISet<string> DisabledPluginIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
