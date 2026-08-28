namespace GeoAssets.Web.Shared;

public partial class RedirectToLogin
{
    protected override void OnInitialized()
        => Nav.NavigateTo("login", replace: true);
}
