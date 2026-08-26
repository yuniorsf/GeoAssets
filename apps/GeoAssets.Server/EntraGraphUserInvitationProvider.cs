using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Server;

/// <summary>
/// <see cref="IUserInvitationProvider"/> implementation backed by Microsoft Graph (XD01-59 Phase
/// 3, XD01-67). Server-only — this class, its <see cref="HttpClient"/>, and the credential
/// behind <see cref="IGraphAccessTokenProvider"/> must never be referenced from
/// <c>GeoAssets.Web</c>/WASM.
/// </summary>
public sealed class EntraGraphUserInvitationProvider : IUserInvitationProvider
{
    private readonly HttpClient _graph;
    private readonly IGraphAccessTokenProvider _tokenProvider;
    private readonly GraphCredentialOptions _credential;

    public EntraGraphUserInvitationProvider(
        HttpClient graph, IGraphAccessTokenProvider tokenProvider, GraphCredentialOptions credential)
    {
        _graph         = graph;
        _tokenProvider = tokenProvider;
        _credential    = credential;
    }

    public async Task<string> CreateInvitedAccountAsync(string email, string displayName, CancellationToken ct = default)
    {
        var body = new
        {
            accountEnabled = true,
            displayName,
            creationType = "LocalAccount",
            passwordProfile = new
            {
                password = GenerateRandomPassword(),
                forceChangePasswordNextSignIn = true,
            },
            identities = new[]
            {
                new
                {
                    signInType       = "emailAddress",
                    issuer           = _credential.TenantDomain,
                    issuerAssignedId = email,
                },
            },
        };

        var response = await SendAsync(HttpMethod.Post, "users", body, ct);
        var dto = await response.Content.ReadFromJsonAsync<GraphIdResponse>(ct)
            ?? throw new InvalidOperationException("POST users returned an empty response.");
        return dto.Id;
    }

    public async Task RevokeInvitedAccountAsync(string externalObjectId, CancellationToken ct = default)
        => await SendAsync(HttpMethod.Patch, $"users/{externalObjectId}", new { accountEnabled = false }, ct);

    // ── Graph mechanics ───────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativeUrl, object body, CancellationToken ct)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(ct);
        var request = new HttpRequestMessage(method, relativeUrl)
        {
            Content = JsonContent.Create(body),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

        var response = await _graph.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return response;
    }

    // Never used to sign in — forceChangePasswordNextSignIn always forces a reset via the
    // invitation flow, so this only needs to satisfy Graph's creation-time complexity validation
    // (Entra's default policy requires 3 of 4 character classes), not be memorable.
    private static string GenerateRandomPassword()
        => $"Xd7!{Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))}";

    // ── Graph wire shapes (minimal — only the fields this provider reads) ──────

    private sealed record GraphIdResponse([property: JsonPropertyName("id")] string Id);
}
