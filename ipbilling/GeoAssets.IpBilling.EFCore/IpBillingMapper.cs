using GeoAssets.IpBilling.Fiscalizacao;
using GeoAssets.IpBilling.Levantamento;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Oficio;
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

    // ── FaturamentoPosting ───────────────────────────────────────────────────

    public static FaturamentoPosting ToDomain(FaturamentoPostingRecord r) => new()
    {
        Id                 = r.Id,
        ServiceOrderId     = r.ServiceOrderId,
        MunicipioId        = r.MunicipioId,
        Competencia        = r.Competencia,
        Mode               = r.Mode,
        DeltaOrBaselineKwh = r.DeltaOrBaselineKwh,
        ResultingKwh       = r.ResultingKwh,
        PostedAt           = r.PostedAt,
    };

    // ── LevantamentoHeader ───────────────────────────────────────────────────

    public static LevantamentoHeader ToDomain(LevantamentoHeaderRecord r) => new()
    {
        ServiceOrderId = r.ServiceOrderId,
        MunicipioId    = r.MunicipioId,
        Competencia    = r.Competencia,
        IsValidated    = r.IsValidated,
    };

    public static LevantamentoHeaderRecord ToRecord(LevantamentoHeader h) => new()
    {
        ServiceOrderId = h.ServiceOrderId,
        MunicipioId    = h.MunicipioId,
        Competencia    = h.Competencia,
        IsValidated    = h.IsValidated,
    };

    // ── LevantamentoTransformerLine / LevantamentoLampDetail ────────────────

    public static LevantamentoTransformerLine ToDomain(LevantamentoTransformerLineRecord r) => new()
    {
        Id                   = r.Id,
        ServiceOrderId       = r.ServiceOrderId,
        CodigoInstalacao     = r.CodigoInstalacao,
        TransformerFeatureId = r.TransformerFeatureId,
        IsNewTransformer     = r.IsNewTransformer,
        Tipo                 = r.Tipo,
        NormalKwh            = r.NormalKwh,
        RetroativoKwh        = r.RetroativoKwh,
        LampDetails          = r.LampDetails.Select(ToDomain).ToList(),
    };

    public static LevantamentoTransformerLineRecord ToRecord(LevantamentoTransformerLine l)
    {
        var record = new LevantamentoTransformerLineRecord
        {
            Id                   = l.Id,
            ServiceOrderId       = l.ServiceOrderId,
            CodigoInstalacao     = l.CodigoInstalacao,
            TransformerFeatureId = l.TransformerFeatureId,
            IsNewTransformer     = l.IsNewTransformer,
            Tipo                 = l.Tipo,
            NormalKwh            = l.NormalKwh,
            RetroativoKwh        = l.RetroativoKwh,
        };
        record.LampDetails.AddRange(l.LampDetails.Select(ToRecord));
        return record;
    }

    public static LevantamentoLampDetail ToDomain(LevantamentoLampDetailRecord r) => new()
    {
        Id                = r.Id,
        TransformerLineId = r.TransformerLineId,
        Code              = r.Code,
        WattageW          = r.WattageW,
        Quantity          = r.Quantity,
    };

    public static LevantamentoLampDetailRecord ToRecord(LevantamentoLampDetail d) => new()
    {
        Id                = d.Id,
        TransformerLineId = d.TransformerLineId,
        Code              = d.Code,
        WattageW          = d.WattageW,
        Quantity          = d.Quantity,
    };

    // ── Levantamento24hLine ──────────────────────────────────────────────────

    public static Levantamento24hLine ToDomain(Levantamento24hLineRecord r) => new()
    {
        Id             = r.Id,
        ServiceOrderId = r.ServiceOrderId,
        FeatureId      = r.FeatureId,
        Code           = r.Code,
        WattageW       = r.WattageW,
        Quantity       = r.Quantity,
        Kwh            = r.Kwh,
    };

    public static Levantamento24hLineRecord ToRecord(Levantamento24hLine l) => new()
    {
        Id             = l.Id,
        ServiceOrderId = l.ServiceOrderId,
        FeatureId      = l.FeatureId,
        Code           = l.Code,
        WattageW       = l.WattageW,
        Quantity       = l.Quantity,
        Kwh            = l.Kwh,
    };

    // ── FiscalizacaoHeader ───────────────────────────────────────────────────

    public static FiscalizacaoHeader ToDomain(FiscalizacaoHeaderRecord r) => new()
    {
        ServiceOrderId = r.ServiceOrderId,
        MunicipioId    = r.MunicipioId,
        Competencia    = r.Competencia,
        IsValidated    = r.IsValidated,
    };

    public static FiscalizacaoHeaderRecord ToRecord(FiscalizacaoHeader h) => new()
    {
        ServiceOrderId = h.ServiceOrderId,
        MunicipioId    = h.MunicipioId,
        Competencia    = h.Competencia,
        IsValidated    = h.IsValidated,
    };

    // ── FiscalizacaoLine / FiscalizacaoOperationGroupDetail ─────────────────

    public static FiscalizacaoLine ToDomain(FiscalizacaoLineRecord r) => new()
    {
        Id               = r.Id,
        ServiceOrderId   = r.ServiceOrderId,
        CodigoInstalacao = r.CodigoInstalacao,
        Agregado         = r.Agregado,
        Retroativo       = r.Retroativo,
        OperationGroups  = r.OperationGroups.Select(ToDomain).ToList(),
    };

    public static FiscalizacaoLineRecord ToRecord(FiscalizacaoLine l)
    {
        var record = new FiscalizacaoLineRecord
        {
            Id               = l.Id,
            ServiceOrderId   = l.ServiceOrderId,
            CodigoInstalacao = l.CodigoInstalacao,
            Agregado         = l.Agregado,
            Retroativo       = l.Retroativo,
        };
        record.OperationGroups.AddRange(l.OperationGroups.Select(ToRecord));
        return record;
    }

    public static FiscalizacaoOperationGroupDetail ToDomain(FiscalizacaoOperationGroupDetailRecord r) => new()
    {
        Id             = r.Id,
        LineId         = r.LineId,
        OperationGroup = r.OperationGroup,
        Count          = r.Count,
    };

    public static FiscalizacaoOperationGroupDetailRecord ToRecord(FiscalizacaoOperationGroupDetail d) => new()
    {
        Id             = d.Id,
        LineId         = d.LineId,
        OperationGroup = d.OperationGroup,
        Count          = d.Count,
    };

    // ── OficioHeader ─────────────────────────────────────────────────────────

    public static OficioHeader ToDomain(OficioHeaderRecord r) => new()
    {
        Id               = r.Id,
        ServiceOrderId   = r.ServiceOrderId,
        MunicipioId      = r.MunicipioId,
        Competencia      = r.Competencia,
        NumeroPrefeitura = r.NumeroPrefeitura,
        IsValidated      = r.IsValidated,
    };

    public static OficioHeaderRecord ToRecord(OficioHeader h) => new()
    {
        Id               = h.Id,
        ServiceOrderId   = h.ServiceOrderId,
        MunicipioId      = h.MunicipioId,
        Competencia      = h.Competencia,
        NumeroPrefeitura = h.NumeroPrefeitura,
        IsValidated      = h.IsValidated,
    };

    // ── OficioLine / OficioOperationGroupDetail ─────────────────────────────

    public static OficioLine ToDomain(OficioLineRecord r) => new()
    {
        Id               = r.Id,
        ServiceOrderId   = r.ServiceOrderId,
        CodigoInstalacao = r.CodigoInstalacao,
        Agregado         = r.Agregado,
        Retroativo       = r.Retroativo,
        OperationGroups  = r.OperationGroups.Select(ToDomain).ToList(),
    };

    public static OficioLineRecord ToRecord(OficioLine l)
    {
        var record = new OficioLineRecord
        {
            Id               = l.Id,
            ServiceOrderId   = l.ServiceOrderId,
            CodigoInstalacao = l.CodigoInstalacao,
            Agregado         = l.Agregado,
            Retroativo       = l.Retroativo,
        };
        record.OperationGroups.AddRange(l.OperationGroups.Select(ToRecord));
        return record;
    }

    public static OficioOperationGroupDetail ToDomain(OficioOperationGroupDetailRecord r) => new()
    {
        Id             = r.Id,
        LineId         = r.LineId,
        OperationGroup = r.OperationGroup,
        Count          = r.Count,
    };

    public static OficioOperationGroupDetailRecord ToRecord(OficioOperationGroupDetail d) => new()
    {
        Id             = d.Id,
        LineId         = d.LineId,
        OperationGroup = d.OperationGroup,
        Count          = d.Count,
    };
}
