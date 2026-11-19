using GeoAssets.IpBilling.Models;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.IpBilling.UI.Components;

public partial class FaturamentoLedgerTable
{
    [Parameter] public IReadOnlyList<Faturamento> Rows { get; set; } = [];

    /// <summary>Resolves a município's display label for a row, so this component doesn't
    /// need its own repository dependency — the page already holds the loaded list.</summary>
    [Parameter] public Func<Guid, string> MunicipioLabel { get; set; } = id => id.ToString();

    [Parameter] public EventCallback<Faturamento> OnRecargaRequested { get; set; }
}
