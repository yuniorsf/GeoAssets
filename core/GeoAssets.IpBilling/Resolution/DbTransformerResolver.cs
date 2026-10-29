using GeoAssets.IpBilling.Repositories;

namespace GeoAssets.IpBilling.Resolution;

/// <summary>
/// Resolves a <c>(município IBGE code, código de instalação)</c> pair to the backing
/// <c>Transformador (IP)</c> <c>GeoFeature.Id</c> via <see cref="IIpTransformerIndexRepository"/>.
/// </summary>
public sealed class DbTransformerResolver(IIpTransformerIndexRepository indexRepository) : ITransformerResolver
{
    public async Task<Guid?> ResolveTransformerFeatureIdAsync(
        string municipioIbgeCode, string codigoInstalacao, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(municipioIbgeCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(codigoInstalacao);

        var entry = await indexRepository.GetAsync(municipioIbgeCode, codigoInstalacao, ct).ConfigureAwait(false);
        return entry?.TransformerFeatureId;
    }
}

