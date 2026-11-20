namespace GeoAssets.IpBilling.GrantSync;

/// <summary>What a queued município grant-sync request asks the kernel for (XD01-395).</summary>
public enum MunicipioGrantSyncKind
{
    /// <summary>Upsert the Prefeitura's grant for the município (revoking the previous one first when the link moved).</summary>
    Sync,

    /// <summary>Revoke the Prefeitura's grant for the município (Prefeitura cleared, or município deleted).</summary>
    Revoke,
}
