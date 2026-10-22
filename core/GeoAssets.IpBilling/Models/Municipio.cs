namespace GeoAssets.IpBilling.Models;

/// <summary>
/// A Brazilian municipality participating in public-lighting (IP) billing — the unit every
/// IP-billing workflow (Levantamento/Fiscalização/Ofício/Faturamento) is scoped to.
///
/// <see cref="DistribuidoraOrganizationId"/> and <see cref="PrefeituraOrganizationId"/> are
/// two independent peer <c>Organization</c>s, deliberately **not** a parent/child pair — an
/// earlier design draft modeled the município as a synthetic child Organization of the
/// Distribuidora and was rejected on review (see XD01-174's ticket context). A município's
/// Prefeitura is granted a scoped view of its own slice of the Distribuidora's data via an
/// <c>OrganizationGrant</c> whose <c>ScopeId</c> is this município's <see cref="IbgeCode"/>
/// (XD01-177 wires the actual grant provisioning; XD01-174 is the general mechanism).
/// </summary>
public sealed class Municipio
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>7-digit IBGE municipality code — the stable natural key used across every
    /// IP-billing lookup (see <see cref="IpTransformerIndex.MunicipioIbgeCode"/>).</summary>
    public required string IbgeCode { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>2-letter Brazilian state code (e.g. "SP", "RJ").</summary>
    public string Uf { get; set; } = string.Empty;

    /// <summary>The utility Organization that owns this município's transformers/poles/feature data. Required — every município belongs to exactly one Distribuidora.</summary>
    public required Guid DistribuidoraOrganizationId { get; set; }

    /// <summary>The municipality's own Organization, paying for the billed kWh. Null until
    /// the Prefeitura side has been onboarded as its own tenant.</summary>
    public Guid? PrefeituraOrganizationId { get; set; }

    /// <summary>Whether Levantamento (full-census survey) data currently arrives via
    /// spreadsheet upload or a dispatched service order.</summary>
    public OrigemWorkflow OrigemLevantamento { get; set; } = OrigemWorkflow.Planilha;

    /// <summary>Whether Fiscalização (spot-check inspection) data currently arrives via
    /// spreadsheet upload or a dispatched service order.</summary>
    public OrigemWorkflow OrigemFiscalizacao { get; set; } = OrigemWorkflow.Planilha;

    /// <summary>Whether Ofício (monthly intervention compilation) data currently arrives via
    /// spreadsheet upload or a dispatched service order.</summary>
    public OrigemWorkflow OrigemOficio { get; set; } = OrigemWorkflow.Planilha;

    /// <summary>Whether the monthly Faturamento ledger consolidation ("Ordem Memória de
    /// Cálculo" in the out-of-scope graduation path) currently arrives via spreadsheet
    /// upload or a dispatched service order.</summary>
    public OrigemWorkflow OrigemMemoriaCalculo { get; set; } = OrigemWorkflow.Planilha;
}
