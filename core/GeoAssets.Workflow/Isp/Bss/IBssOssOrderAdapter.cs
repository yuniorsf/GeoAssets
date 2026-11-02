using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.Bss;

/// <summary>
/// One BSS/OSS vendor's translation between its own webhook payload shape and this codebase's
/// generic FTTH drop-install order lifecycle (XD01-280's AC3: adding a new provider means adding
/// a new implementation of this interface and registering it — never touching an existing one).
/// </summary>
public interface IBssOssOrderAdapter
{
    /// <summary>Case-insensitively matched against the inbound webhook's <c>ProviderId</c> and the order's stored <see cref="BssOrderAttributes.ProviderIdKey"/>.</summary>
    string ProviderId { get; }

    /// <summary>Throws <see cref="BssOrderSpawnParseException"/> if <paramref name="payloadJson"/> is malformed or missing a required field.</summary>
    BssOrderSpawnSpec ParseOrderSpawn(string payloadJson);

    /// <summary>
    /// Builds the outbound activation-callback JSON for a FTTH drop-install order that just
    /// reached <see cref="FtthDropInstallStatus.Activated"/>. Throws <see cref="InvalidOperationException"/>
    /// if a required attribute (e.g. the scanned ONT FSAN/MAC) isn't recorded on
    /// <paramref name="completedOrder"/> — a drop that reached Activated without them indicates
    /// the checklist wasn't actually completed, not something to silently paper over.
    /// </summary>
    string BuildActivationPayload(IServiceOrder completedOrder);
}
