using GeoAssets.IpBilling.Models;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.IpBilling;

public partial class MunicipioList
{
    [Parameter] public Guid? SelectedMunicipioId { get; set; }
    [Parameter] public EventCallback<Municipio> OnMunicipioSelected { get; set; }
    [Parameter] public EventCallback OnCreateRequested { get; set; }

    private List<Municipio> _municipios = [];
    private bool _loading = true;

    protected override Task OnInitializedAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        _loading = true;
        StateHasChanged();
        _municipios = [.. await Repository.GetAllAsync()];
        _loading = false;
        StateHasChanged();
    }

    private Task SelectMunicipio(Municipio municipio) => OnMunicipioSelected.InvokeAsync(municipio);
}
