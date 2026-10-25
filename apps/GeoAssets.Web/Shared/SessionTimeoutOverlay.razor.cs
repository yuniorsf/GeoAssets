namespace GeoAssets.Web.Shared;

public partial class SessionTimeoutOverlay
{
    protected override void OnInitialized()
        => SessionTimeout.OnStateChanged += Refresh;

    private void StayLoggedIn()
        => SessionTimeout.RecordActivity();

    private void Refresh()
        => _ = InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
            SessionTimeout.OnStateChanged -= Refresh;
    }
}
