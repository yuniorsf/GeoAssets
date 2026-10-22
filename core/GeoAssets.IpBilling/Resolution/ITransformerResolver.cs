namespace GeoAssets.IpBilling.Resolution;

/// <summary>
/// Resolves the "Transformador (IP)" <c>GeoFeature</c> backing a (município IBGE code,
/// código de instalação) pair, as read off an uploaded spreadsheet row — backed by
/// <see cref="Models.IpTransformerIndex"/>. Concrete implementation lands with Phase 1
/// (XD01-179); this ticket only adds the interface so later calculation-engine code can
/// depend on the abstraction from day one.
/// </summary>
public interface ITransformerResolver
{
    /// <summary>Returns the transformer's <c>GeoFeature.Id</c>, or null when no index row matches.</summary>
    Task<Guid?> ResolveTransformerFeatureIdAsync(
        string municipioIbgeCode, string codigoInstalacao, CancellationToken ct = default);
}
