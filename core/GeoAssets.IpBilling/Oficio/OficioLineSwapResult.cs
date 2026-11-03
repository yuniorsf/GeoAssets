namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// One line's swap ("troca") flag, as derived by <see cref="OficioLineSwapView.Compute"/>.
/// </summary>
public sealed record OficioLineSwapResult(Guid LineId, string CodigoInstalacao, bool IsSwap);
