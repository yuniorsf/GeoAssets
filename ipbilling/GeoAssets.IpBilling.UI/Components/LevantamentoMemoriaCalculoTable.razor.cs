using GeoAssets.IpBilling.Levantamento;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.IpBilling.UI.Components;

public partial class LevantamentoMemoriaCalculoTable
{
    [Parameter, EditorRequired] public LevantamentoMemoriaCalculoDto Memoria { get; set; } = null!;
}
