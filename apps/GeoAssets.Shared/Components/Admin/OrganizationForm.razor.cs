using System.ComponentModel.DataAnnotations;
using GeoAssets.Identity.Authorization.Models;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Admin;

public partial class OrganizationForm
{
    [Parameter] public Guid? OrganizationId { get; set; }
    [Parameter] public bool IsNew { get; set; }
    [Parameter] public EventCallback<Guid> OnSaved { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private sealed class OrganizationEditModel
    {
        [Required] public string Name { get; set; } = string.Empty;
        [Required] public string Slug { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
    }

    private OrganizationEditModel _editModel = new();
    private Organization? _organization;
    private bool _loading = true;
    private string? _error;
    private Guid? _loadedOrganizationId;

    protected override async Task OnParametersSetAsync()
    {
        if (IsNew)
        {
            _loadedOrganizationId = null;
            _organization = null;
            _editModel = new OrganizationEditModel();
            _loading = false;
            return;
        }

        if (OrganizationId is null || OrganizationId == _loadedOrganizationId) return;

        _loading = true;
        StateHasChanged();

        _loadedOrganizationId = OrganizationId;
        _organization = await Repository.GetByIdAsync(OrganizationId.Value);
        if (_organization is not null)
        {
            _editModel = new OrganizationEditModel
            {
                Name        = _organization.Name,
                Slug        = _organization.Slug,
                Description = _organization.Description,
                IsActive    = _organization.IsActive,
            };
        }
        _loading = false;
    }

    private async Task HandleSave()
    {
        _error = null;

        try
        {
            if (IsNew)
            {
                var organization = new Organization
                {
                    Id          = Guid.NewGuid(),
                    Name        = _editModel.Name,
                    Slug        = _editModel.Slug,
                    Description = _editModel.Description,
                    IsActive    = _editModel.IsActive,
                    CreatedAt   = DateTime.UtcNow,
                };
                await Repository.AddAsync(organization);
                await Repository.SaveChangesAsync();
                await OnSaved.InvokeAsync(organization.Id);
            }
            else if (_organization is not null)
            {
                _organization.Name        = _editModel.Name;
                _organization.Slug        = _editModel.Slug;
                _organization.Description = _editModel.Description;
                _organization.IsActive    = _editModel.IsActive;
                await Repository.UpdateAsync(_organization);
                await Repository.SaveChangesAsync();
                await OnSaved.InvokeAsync(_organization.Id);
            }
        }
        catch (Exception ex)
        {
            // A duplicate slug or similar server-side rejection must not take down the form —
            // surface it so the admin can correct the fields and retry.
            _error = L.GetString("admin.organizations.saveError", ex.Message);
        }
    }

    private async Task Cancel() => await OnCancel.InvokeAsync();
}
