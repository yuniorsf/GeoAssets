using GeoAssets.IpBilling.Levantamento;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.IpBilling;

public partial class LevantamentoMemoriaCalculoTable
{
    [Parameter, EditorRequired] public LevantamentoMemoriaCalculoDto Memoria { get; set; } = null!;
}
