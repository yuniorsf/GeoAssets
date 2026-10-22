using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Repositories;

/// <summary>
/// Read access to the seeded, non-user-editable <see cref="LampType"/>/<see cref="LampWattageOption"/>
/// reference catalog. No write methods — rows are only ever introduced by an EF Core
/// migration (see <c>GeoAssets.IpBilling.Persistence</c>'s initial-create migration), never
/// through application code.
/// </summary>
public interface ILampTypeRepository
{
    Task<LampType?> GetByCodeAsync(LampTypeCode code, CancellationToken ct = default);

    Task<IReadOnlyList<LampType>> GetAllAsync(CancellationToken ct = default);
}
