using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Persistence.Entities;

namespace GeoAssets.IpBilling.Persistence;

/// <summary>
/// Converts between the domain models in <c>GeoAssets.IpBilling.Models</c> and their EF
/// entities in <see cref="Entities"/> — kept separate so the domain models stay free of EF
/// annotations/infrastructure concerns, same convention as <c>ProjectMapper</c>/<c>OrderTypeMapper</c>.
/// </summary>
internal static class IpBillingMapper
{
    // ── LampType / LampWattageOption ──────────────────────────────────────────

    public static LampType ToDomain(LampTypeRecord r) => new()
    {
        Id             = r.Id,
        Code           = r.Code,
        Name           = r.Name,
        WattageOptions = r.WattageOptions.Select(ToDomain).ToList(),
    };

    public static LampWattageOption ToDomain(LampWattageOptionRecord r) => new()
    {
        Id           = r.Id,
        LampTypeId   = r.LampTypeId,
        WattageW     = r.WattageW,
        BallastLossW = r.BallastLossW,
    };

    // ── Municipio ──────────────────────────────────────────────────────────────

    public static Municipio ToDomain(MunicipioRecord r) => new()
    {
        Id                          = r.Id,
        IbgeCode                    = r.IbgeCode,
        Name                        = r.Name,
        Uf                          = r.Uf,
        DistribuidoraOrganizationId = r.DistribuidoraOrganizationId,
        PrefeituraOrganizationId    = r.PrefeituraOrganizationId,
        OrigemLevantamento          = r.OrigemLevantamento,
        OrigemFiscalizacao          = r.OrigemFiscalizacao,
        OrigemOficio                = r.OrigemOficio,
        OrigemMemoriaCalculo        = r.OrigemMemoriaCalculo,
    };

    public static MunicipioRecord ToRecord(Municipio m) => new()
    {
        Id                          = m.Id,
        IbgeCode                    = m.IbgeCode,
        Name                        = m.Name,
        Uf                          = m.Uf,
        DistribuidoraOrganizationId = m.DistribuidoraOrganizationId,
        PrefeituraOrganizationId    = m.PrefeituraOrganizationId,
        OrigemLevantamento          = m.OrigemLevantamento,
        OrigemFiscalizacao          = m.OrigemFiscalizacao,
        OrigemOficio                = m.OrigemOficio,
        OrigemMemoriaCalculo        = m.OrigemMemoriaCalculo,
    };

    // ── UnidadeOrganizacional ────────────────────────────────────────────────

    public static UnidadeOrganizacional ToDomain(UnidadeOrganizacionalRecord r) => new()
    {
        Id          = r.Id,
        MunicipioId = r.MunicipioId,
        Name        = r.Name,
        Code        = r.Code,
    };

    public static UnidadeOrganizacionalRecord ToRecord(UnidadeOrganizacional u) => new()
    {
        Id          = u.Id,
        MunicipioId = u.MunicipioId,
        Name        = u.Name,
        Code        = u.Code,
    };

    // ── IpTransformerIndex ───────────────────────────────────────────────────

    public static IpTransformerIndex ToDomain(IpTransformerIndexRecord r) => new()
    {
        Id                   = r.Id,
        MunicipioIbgeCode    = r.MunicipioIbgeCode,
        CodigoInstalacao     = r.CodigoInstalacao,
        TransformerFeatureId = r.TransformerFeatureId,
    };

    public static IpTransformerIndexRecord ToRecord(IpTransformerIndex i) => new()
    {
        Id                   = i.Id,
        MunicipioIbgeCode    = i.MunicipioIbgeCode,
        CodigoInstalacao     = i.CodigoInstalacao,
        TransformerFeatureId = i.TransformerFeatureId,
    };

    // ── Faturamento ──────────────────────────────────────────────────────────

    public static Faturamento ToDomain(FaturamentoRecord r) => new()
    {
        Id          = r.Id,
        MunicipioId = r.MunicipioId,
        Competencia = r.Competencia,
        KwhFaturado = r.KwhFaturado,
        CreatedAt   = r.CreatedAt,
    };

    public static FaturamentoRecord ToRecord(Faturamento f) => new()
    {
        Id          = f.Id,
        MunicipioId = f.MunicipioId,
        Competencia = f.Competencia,
        KwhFaturado = f.KwhFaturado,
        CreatedAt   = f.CreatedAt,
    };
}
