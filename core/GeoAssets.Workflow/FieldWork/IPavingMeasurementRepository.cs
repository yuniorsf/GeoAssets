namespace GeoAssets.Workflow.FieldWork;

/// <summary>Persistence abstraction for <see cref="PavingMeasurement"/> (XD01-291). Append-only, same convention as <see cref="Contractors.IContractorUsPostingRepository"/>.</summary>
public interface IPavingMeasurementRepository
{
    Task<PavingMeasurement?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<PavingMeasurement>> GetByParentServiceOrderIdAsync(string parentServiceOrderId, CancellationToken ct = default);

    Task AddAsync(PavingMeasurement measurement, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
