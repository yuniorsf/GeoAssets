namespace GeoAssets.Workflow.Erp.Sienge;

/// <summary>
/// Maps machine-hour and hauling-ticket records onto Sienge's unified cost-center posting line
/// shape (XD01-254's "Machine-hour and hauling-ticket records sync correctly against Sienge cost
/// centers" AC). Pure, no I/O — records are pulled by the caller.
/// </summary>
public static class SiengeCostCenterPostingMapper
{
    public static IReadOnlyList<SiengeCostCenterPostingLine> Map(
        IReadOnlyList<MachineHourRecord> machineHours,
        IReadOnlyList<HaulingTicketRecord> haulingTickets)
    {
        ArgumentNullException.ThrowIfNull(machineHours);
        ArgumentNullException.ThrowIfNull(haulingTickets);

        var lines = new List<SiengeCostCenterPostingLine>(machineHours.Count + haulingTickets.Count);

        foreach (var record in machineHours)
        {
            lines.Add(new SiengeCostCenterPostingLine(record.CostCenterCode, record.UsCode, record.Hours, record.WorkDateUtc));
        }

        foreach (var record in haulingTickets)
        {
            lines.Add(new SiengeCostCenterPostingLine(record.CostCenterCode, record.UsCode, record.LoadVolumeCubicMeters, record.HaulDateUtc));
        }

        return lines;
    }
}
