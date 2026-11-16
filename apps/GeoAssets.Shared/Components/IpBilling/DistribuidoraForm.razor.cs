using System.ComponentModel.DataAnnotations;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.IpBilling.Models;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.IpBilling;

/// <summary>Create/edit/delete form for one <see cref="Distribuidora"/> (XD01-301).</summary>
public partial class DistribuidoraForm
{
    [Parameter] public Guid? DistribuidoraId { get; set; }
    [Parameter] public bool IsNew { get; set; }
    [Parameter] public EventCallback<Guid> OnSaved { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }
    [Parameter] public EventCallback OnDeleted { get; set; }

    private sealed class DistribuidoraEditModel
    {
        [Required] public string Name { get; set; } = string.Empty;
        public Guid OrganizationId { get; set; }
    }

    private DistribuidoraEditModel _editModel = new();
    private Distribuidora? _distribuidora;
    private List<Organization> _organizations = [];
    private bool _loading = true;
    private string? _error;
    private Guid? _loadedDistribuidoraId;

    protected override async Task OnInitializedAsync()
    {
        _organizations = [.. await OrganizationRepository.GetAllAsync()];
    }

    protected override async Task OnParametersSetAsync()
    {
        if (IsNew)
        {
            _loadedDistribuidoraId = null;
            _distribuidora = null;
            _editModel = new DistribuidoraEditModel();
            _loading = false;
            return;
        }

        if (DistribuidoraId is null || DistribuidoraId == _loadedDistribuidoraId) return;

        _loading = true;
        StateHasChanged();

        _loadedDistribuidoraId = DistribuidoraId;
        _error = null;
        _distribuidora = await Repository.GetByIdAsync(DistribuidoraId.Value);
        if (_distribuidora is not null)
        {
            _editModel = new DistribuidoraEditModel
            {
                Name = _distribuidora.Name,
                OrganizationId = _distribuidora.OrganizationId,
            };
        }

        _loading = false;
    }

    private void OnOrganizationChanged(ChangeEventArgs e) =>
        _editModel.OrganizationId = Guid.TryParse(e.Value as string, out var id) ? id : Guid.Empty;

    private async Task HandleSaveAsync()
    {
        _error = null;

        if (_editModel.OrganizationId == Guid.Empty)
        {
            _error = L["admin.ipBilling.distribuidoras.organizationRequired"];
            return;
        }

        try
        {
            if (IsNew)
            {
                var distribuidora = new Distribuidora
                {
                    OrganizationId = _editModel.OrganizationId,
                    Name = _editModel.Name,
                };
                await Repository.AddAsync(distribuidora);
                await Repository.SaveChangesAsync();
                await OnSaved.InvokeAsync(distribuidora.Id);
            }
            else if (_distribuidora is not null)
            {
                _distribuidora.OrganizationId = _editModel.OrganizationId;
                _distribuidora.Name = _editModel.Name;
                await Repository.UpdateAsync(_distribuidora);
                await Repository.SaveChangesAsync();
                await OnSaved.InvokeAsync(_distribuidora.Id);
            }
        }
        catch (Exception ex)
        {
            _error = L.GetString("admin.ipBilling.distribuidoras.saveError", ex.Message);
        }
    }

    private async Task HandleDeleteAsync()
    {
        if (_distribuidora is null) return;

        _error = null;
        try
        {
            await Repository.DeleteAsync(_distribuidora.Id);
            await Repository.SaveChangesAsync();
            await OnDeleted.InvokeAsync();
        }
        catch (Exception ex)
        {
            _error = L.GetString("admin.ipBilling.distribuidoras.saveError", ex.Message);
        }
    }

    private Task CancelAsync() => OnCancel.InvokeAsync();
}
