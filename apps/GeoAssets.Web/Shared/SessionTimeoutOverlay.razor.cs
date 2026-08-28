namespace GeoAssets.Web.Shared;

public partial class SessionTimeoutOverlay
{
    protected override void OnInitialized()
        => SessionTimeout.OnStateChanged += Refresh;

    private void StayLoggedIn()
        => SessionTimeout.RecordActivity();

    private void Refresh()
        => InvokeAsync(StateHasChanged);

    public void Dispose()
        => SessionTimeout.OnStateChanged -= Refresh;
}
