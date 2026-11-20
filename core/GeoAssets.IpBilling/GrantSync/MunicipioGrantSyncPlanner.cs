namespace GeoAssets.IpBilling.GrantSync;

/// <summary>
/// Decides which grant change, if any, a município save calls for (XD01-395) — pure, from the grant-relevant fields before
/// and after the save (<see cref="MunicipioGrantLink"/>; <c>null</c> before = created, <c>null</c> after = deleted):
/// <list type="bullet">
///   <item>created with a Prefeitura → <see cref="MunicipioGrantSyncKind.Sync"/>;</item>
///   <item>deleted with a Prefeitura → <see cref="MunicipioGrantSyncKind.Revoke"/> of its grant;</item>
///   <item>updated, Prefeitura cleared → <see cref="MunicipioGrantSyncKind.Revoke"/> of the old grant;</item>
///   <item>updated, the Prefeitura, the Distribuidora or the IBGE code changed and a Prefeitura is set →
///   <see cref="MunicipioGrantSyncKind.Sync"/> of the new grant, naming the old Prefeitura when it changed and the old key
///   when the IBGE code or the Distribuidora moved under an existing link, so the stale grant is revoked;</item>
///   <item>anything else (no Prefeitura either side, or nothing grant-relevant changed) → <c>null</c>: nothing to queue.</item>
/// </list>
/// </summary>
public static class MunicipioGrantSyncPlanner
{
    public static MunicipioGrantChange? Plan(MunicipioGrantLink? before, MunicipioGrantLink? after)
    {
        if (before is not { } old)
        {
            return after is { PrefeituraOrganizationId: not null } created
                ? Sync(created, previousPrefeitura: null, previousKey: null)
                : null;
        }

        if (after is not { } now)
        {
            return old.PrefeituraOrganizationId is null ? null : Revoke(old);
        }

        var keyMoved = !string.Equals(old.IbgeCode, now.IbgeCode, StringComparison.Ordinal)
            || old.DistribuidoraOrganizationId != now.DistribuidoraOrganizationId;
        var prefeituraChanged = old.PrefeituraOrganizationId != now.PrefeituraOrganizationId;

        if (!keyMoved && !prefeituraChanged)
            return null;

        if (now.PrefeituraOrganizationId is null)
            return old.PrefeituraOrganizationId is null ? null : Revoke(old);

        return Sync(
            now,
            previousPrefeitura: prefeituraChanged ? old.PrefeituraOrganizationId : null,
            previousKey: keyMoved && old.PrefeituraOrganizationId is not null ? old : null);
    }

    private static MunicipioGrantChange Sync(MunicipioGrantLink link, Guid? previousPrefeitura, MunicipioGrantLink? previousKey) =>
        new(
            link.MunicipioId,
            MunicipioGrantSyncKind.Sync,
            link.IbgeCode,
            link.DistribuidoraOrganizationId,
            link.PrefeituraOrganizationId,
            previousPrefeitura,
            previousKey?.IbgeCode,
            previousKey?.DistribuidoraOrganizationId);

    private static MunicipioGrantChange Revoke(MunicipioGrantLink link) =>
        new(
            link.MunicipioId,
            MunicipioGrantSyncKind.Revoke,
            link.IbgeCode,
            link.DistribuidoraOrganizationId,
            link.PrefeituraOrganizationId);
}
