namespace GeoAssets.IpBilling.Levantamento;

/// <summary>
/// Default <see cref="IInterveningOperationLookup"/> — always reports no intervening coverage.
/// Correct (not merely a stub) until XD01-182/185 land: with no Fiscalização/Ofício data
/// anywhere yet, there genuinely is nothing to exclude, so every new transformer resolves to
/// the naive codes (00/10) rather than their [FIX] siblings (01/11) — exactly the legacy-faithful
/// behavior for a município with no Fiscalização/Ofício history at all.
/// </summary>
public sealed class NullInterveningOperationLookup : IInterveningOperationLookup
{
    public Task<InterveningOperationCoverage?> GetMostRecentValidatedAsync(
        Guid municipioId, string? codigoInstalacao, DateOnly before, CancellationToken ct = default)
        => Task.FromResult<InterveningOperationCoverage?>(null);
}
