using GeoAssets.Hosting.Abstractions;
using GeoAssets.Server.Modules.NeutralOperator;
using GeoAssets.Server.Modules.PostgresSpatial;

namespace GeoAssets.Server.Modules;

/// <summary>
/// The modules this host is built with (XD01-382), in the quarantine namespace so the kernel's <c>Program</c> names no
/// vertical: which of them run, and in what order, is <c>GeoAssets:Modules</c>'s to say.
/// <list type="bullet">
///   <item><see cref="PostgresSpatialModule"/>: the "postgres" provider plugin, organization catalogs, WMS.</item>
///   <item><see cref="KernelHostedNeutralOperatorModule"/>: shared-use dispatch, pole occupancy staging and the approval
///   saga, clandestine attachments, the ISP tenant portal and the GIS export — the rollback
///   flag of the cut-over to <c>GeoAssets.NeutralOperator.Server</c>.</item>
/// </list>
/// The IP-billing grant bridge left with XD01-385: it is <c>GeoAssets.IpBilling.Server</c>'s, over the kernel's generic
/// organization-grant API.
/// </summary>
public static class ServerModules
{
    /// <summary>A fresh instance of every built-in module.</summary>
    public static IReadOnlyList<IGeoAssetsModule> BuiltIns() =>
        [new PostgresSpatialModule(), new KernelHostedNeutralOperatorModule()];
}
