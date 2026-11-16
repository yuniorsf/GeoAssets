namespace GeoAssets.Workflow.Erp.Sap;

/// <summary>
/// Parses a raw SAP PM inbound webhook payload into <see cref="SapPmOrderSpawnSpec"/>. Pure —
/// no I/O — same split as <see cref="Isp.Bss.IBssOssOrderAdapter"/>.
/// </summary>
public interface ISapPmOrderAdapter
{
    SapPmOrderSpawnSpec ParseWorkOrderNotification(string payloadJson);
}
