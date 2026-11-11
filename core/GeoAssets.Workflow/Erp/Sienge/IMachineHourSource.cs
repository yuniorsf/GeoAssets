namespace GeoAssets.Workflow.Erp.Sienge;

/// <summary>Host-side source of civil-crew equipment-hour claims to sync to Sienge (XD01-254).</summary>
public interface IMachineHourSource
{
    Task<IReadOnlyList<MachineHourRecord>> GetMachineHoursSinceAsync(DateTime sinceUtc, CancellationToken ct = default);
}
