using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Shared.Components.Admin;

public partial class InvitationsPanel
{
    private bool _enabled;
    private bool _loading = true;
    private List<PendingInvitation> _invitations = [];
    private bool _showInviteDialog;
    private bool _showRevokeConfirm;
    private PendingInvitation? _toRevoke;

    protected override async Task OnInitializedAsync()
    {
        // Visible only when both the repository resolves (Rest backend only, XD01-70) and the
        // server reports the feature actually usable (both Graph and ACS wired, XD01-69) —
        // "hide entirely rather than show a broken/no-op control", same as RoleSync's UI gating.
        var invitationRepo = ServiceProvider.GetService<IPendingInvitationRepository>();
        var statusProvider = ServiceProvider.GetService<IInvitationStatusProvider>();
        _enabled = invitationRepo is not null && statusProvider is not null && await statusProvider.IsEnabledAsync();

        if (_enabled) await RefreshAsync();
        else _loading = false;
    }

    public async Task RefreshAsync()
    {
        var invitationRepo = ServiceProvider.GetService<IPendingInvitationRepository>();
        if (invitationRepo is null) return;

        _loading = true;
        StateHasChanged();
        _invitations = [.. await invitationRepo.GetAllPendingAsync()];
        _loading = false;
        StateHasChanged();
    }

    private async Task OnInvited()
    {
        _showInviteDialog = false;
        await RefreshAsync();
    }

    private void RequestRevoke(PendingInvitation invitation)
    {
        _toRevoke = invitation;
        _showRevokeConfirm = true;
    }

    private async Task ConfirmRevokeAsync()
    {
        if (_toRevoke is null) return;
        _showRevokeConfirm = false;

        var client = ServiceProvider.GetService<IInvitationClient>();
        if (client is null) return;

        await client.RevokeInvitationAsync(_toRevoke.Id);
        await RefreshAsync();
    }
}
