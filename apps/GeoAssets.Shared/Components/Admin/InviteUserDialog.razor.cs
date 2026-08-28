using System.ComponentModel.DataAnnotations;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Shared.Components.Admin;

public partial class InviteUserDialog
{
    [Parameter] public bool Visible { get; set; }
    [Parameter] public EventCallback OnInvited { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private sealed class InviteEditModel
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required] public string DisplayName { get; set; } = string.Empty;
    }

    private InviteEditModel _editModel = new();
    private bool _inviting;
    private string? _error;

    protected override void OnParametersSet()
    {
        // Reset whenever the dialog is (re)opened, so a previous attempt's input/error doesn't
        // linger the next time it's shown.
        if (Visible)
        {
            _editModel = new InviteEditModel();
            _error = null;
        }
    }

    private async Task HandleInviteAsync()
    {
        var client = ServiceProvider.GetService<IInvitationClient>();
        if (client is null) return;

        _inviting = true;
        _error = null;
        StateHasChanged();

        try
        {
            await client.CreateInvitationAsync(_editModel.Email, _editModel.DisplayName);
            await OnInvited.InvokeAsync();
        }
        catch (Exception)
        {
            // The Graph/ACS credential or network is genuinely allowed to fail here — surface
            // it without closing the dialog, matching RoleForm's RegisterInEntraAsync precedent.
            _error = L["admin.invitations.inviteError"];
        }
        finally
        {
            _inviting = false;
        }
    }

    private async Task Cancel() => await OnCancel.InvokeAsync();
}
