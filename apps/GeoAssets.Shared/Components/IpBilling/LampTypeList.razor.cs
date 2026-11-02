using GeoAssets.IpBilling.Models;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.IpBilling;

public partial class LampTypeList
{
    [Parameter] public Guid? SelectedLampTypeId { get; set; }
    [Parameter] public EventCallback<LampType> OnLampTypeSelected { get; set; }

    private List<LampType> _lampTypes = [];
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        _loading = true;
        _lampTypes = [.. await Repository.GetAllAsync()];
        _loading = false;
    }

    private Task SelectLampType(LampType lampType) => OnLampTypeSelected.InvokeAsync(lampType);
}
