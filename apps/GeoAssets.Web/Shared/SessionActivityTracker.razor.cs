using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.JSInterop;

namespace GeoAssets.Web.Shared;

public partial class SessionActivityTracker
{
    private DotNetObjectReference<SessionActivityTracker>? _ref;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        _ref = DotNetObjectReference.Create(this);
        await JS.InvokeVoidAsync("sessionActivity.init", _ref);

        SessionTimeout.OnTimeout += HandleTimeout;
        SessionTimeout.Start();
    }

    /// <summary>Called from JavaScript whenever the user interacts with the page.</summary>
    [JSInvokable]
    public void OnUserActivity() => SessionTimeout.RecordActivity();

    private void HandleTimeout()
        => Nav.NavigateToLogout("authentication/logout");

    public async ValueTask DisposeAsync()
    {
        SessionTimeout.OnTimeout -= HandleTimeout;

        if (_ref is not null)
        {
            try { await JS.InvokeVoidAsync("sessionActivity.dispose"); } catch { }
            _ref.Dispose();
        }
    }
}
