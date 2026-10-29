using GeoAssets.IpBilling.Models;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.IpBilling.Workflow;

/// <summary>
/// Canonical <see cref="OrderType"/> definitions for the three <c>GEOS_FAT_IP</c> billing
/// workflows: Levantamento (<see cref="FaturamentoPostingMode.Set"/> baseline), Fiscalização
/// (<see cref="FaturamentoPostingMode.Accumulate"/> delta), and Ofício
/// (<see cref="FaturamentoPostingMode.Accumulate"/> delta).
/// </summary>
public static class IpBillingOrderTypes
{
    public const string LevantamentoId = "ip-levantamento";
    public const string FiscalizacaoId = "ip-fiscalizacao";
    public const string OficioId       = "ip-oficio";

    public static OrderType CreateLevantamento() => new()
    {
        Id              = LevantamentoId,
        DisplayName     = "Levantamento (IP)",
        Description     = "Levantamento cadastral de iluminação pública (define linha de base mensal).",
        InitialStateKey = IpBillingWorkflowGraph.Carregado,
        States          = IpBillingWorkflowGraph.States(),
        Transitions     = IpBillingWorkflowGraph.Transitions(),
    };

    public static OrderType CreateFiscalizacao() => new()
    {
        Id              = FiscalizacaoId,
        DisplayName     = "Fiscalização (IP)",
        Description     = "Fiscalização de campo de iluminação pública (acumula delta mensal).",
        InitialStateKey = IpBillingWorkflowGraph.Carregado,
        States          = IpBillingWorkflowGraph.States(),
        Transitions     = IpBillingWorkflowGraph.Transitions(),
    };

    public static OrderType CreateOficio() => new()
    {
        Id              = OficioId,
        DisplayName     = "Ofício (IP)",
        Description     = "Ofício municipal de iluminação pública (acumula delta mensal).",
        InitialStateKey = IpBillingWorkflowGraph.Carregado,
        States          = IpBillingWorkflowGraph.States(),
        Transitions     = IpBillingWorkflowGraph.Transitions(),
    };

    public static IReadOnlyList<OrderType> All() =>
    [
        CreateLevantamento(),
        CreateFiscalizacao(),
        CreateOficio(),
    ];

    public static void RegisterAll(OrderTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        foreach (var orderType in All())
        {
            registry.Register(orderType);
        }
    }

    public static FaturamentoPostingMode PostingModeFor(string orderTypeId) =>
        string.Equals(orderTypeId, LevantamentoId, StringComparison.Ordinal)
            ? FaturamentoPostingMode.Set
            : FaturamentoPostingMode.Accumulate;
}
