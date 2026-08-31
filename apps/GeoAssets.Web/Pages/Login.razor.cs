using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace GeoAssets.Web.Pages;

public partial class Login
{
    private string? _errorMessage;

    protected override async Task OnInitializedAsync()
    {
        // If already authenticated, redirect to home
        var state = await AuthStateProvider.GetAuthenticationStateAsync();
        if (state.User.Identity?.IsAuthenticated == true)
            Nav.NavigateTo("/");
    }

    private void SignIn()
    {
        try
        {
            Nav.NavigateToLogin("authentication/login");
        }
        catch (Exception ex)
        {
            _errorMessage = $"Error al iniciar sesión: {ex.Message}";
        }
    }
}
