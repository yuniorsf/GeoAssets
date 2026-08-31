using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Shared.Pages;

public partial class CompleteProfile
{
    // Matches the ticket's own example — the only field this phase collects (deferred to
    // implementation, not architecture, per the ticket's scope notes).
    private const string PhoneClaimType = "phone";

    private sealed class ProfileEditModel
    {
        public string Phone { get; set; } = string.Empty;
    }

    private ProfileEditModel _editModel = new();
    private bool _loading = true;
    private bool _saving;
    private string? _error;
    private Guid? _appUserId;
    private PendingInvitation? _pendingInvitation;

    protected override async Task OnInitializedAsync()
    {
        var current = await CurrentUserAccessor.GetCurrentUserAsync();
        if (current is null || string.IsNullOrEmpty(current.ExternalObjectId))
        {
            _loading = false;
            return;
        }

        // The redirect gate (UserProvisioningService) only ever sends someone here after
        // provisioning them. IGeoAuthorizationService.GetAuthorizationContextAsync() — the
        // same self-service "who am I" call every other page uses — resolves (JIT-provisioning
        // if needed, per GeoAuthorizationService) the caller's own AppUser id. Unlike
        // IUserRepository.GetByExternalObjectIdAsync, this works under both the InMemory and
        // Rest backends: RestUserRepository has no matching server endpoint for that lookup
        // and throws NotSupportedException (XD01-93).
        _appUserId = await ResolveAppUserIdAsync(AuthService);

        // Optionally resolved — never registered under Identity:Backend=InMemory (XD01-70/-71).
        var invitationRepo = ServiceProvider.GetService<IPendingInvitationRepository>();
        _pendingInvitation = invitationRepo is null
            ? null
            : await invitationRepo.GetByExternalObjectIdAsync(current.ExternalObjectId);

        _loading = false;
    }

    /// <summary>
    /// Resolves the current caller's own <see cref="AppUser.Id"/> via the shared authorization
    /// context — the only lookup that works under both the InMemory and Rest backends (XD01-93).
    /// </summary>
    public static async Task<Guid> ResolveAppUserIdAsync(
        IGeoAuthorizationService authService,
        CancellationToken        ct = default)
    {
        var ctx = await authService.GetAuthorizationContextAsync(ct);
        return ctx.User.Id;
    }

    private async Task HandleSaveAsync()
    {
        _saving = true;
        _error = null;
        StateHasChanged();

        try
        {
            if (_appUserId is { } appUserId && !string.IsNullOrWhiteSpace(_editModel.Phone))
            {
                await ClaimRepository.AddAsync(new UserClaim
                {
                    UserId = appUserId,
                    Type   = PhoneClaimType,
                    Value  = _editModel.Phone,
                });
                await ClaimRepository.SaveChangesAsync();
            }

            if (_pendingInvitation is not null)
            {
                var invitationClient = ServiceProvider.GetService<IInvitationClient>();
                if (invitationClient is not null)
                    await invitationClient.RedeemInvitationAsync(_pendingInvitation.Id);
            }

            Navigation.NavigateTo("/");
        }
        catch (Exception)
        {
            _error = L["admin.completeProfile.saveError"];
        }
        finally
        {
            _saving = false;
        }
    }
}
