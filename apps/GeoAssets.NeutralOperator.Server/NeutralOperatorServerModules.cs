using GeoAssets.Hosting.Abstractions;
using GeoAssets.NeutralOperator.Modules;

namespace GeoAssets.NeutralOperator.Server;

/// <summary>
/// The modules this host is built with (XD01-382): the NeutralOperator vertical, which <c>GeoAssets:Modules</c> lists by
/// default (appsettings.json). Unlike the kernel's composition, it carries no in-process asset-mutation apply — this host
/// serves no asset catalog: the asset mutation batches an approval stages stay on its outbox for the kernel (the approval
/// saga, XD01-383) — and no GIS export.
/// </summary>
internal static class NeutralOperatorServerModules
{
    public static IReadOnlyList<IGeoAssetsModule> BuiltIns() => [new NeutralOperatorModule()];
}
