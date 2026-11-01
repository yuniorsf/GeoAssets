using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GeoAssets.Core.Services.Evidence;
using GeoAssets.FieldService.Server;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// Integration coverage for <c>EvidenceVerificationRestApiExtensions</c> (XD01-278) — the HTTP
/// façade only. <see cref="EvidenceVerificationService.Verify"/>'s own re-derive-and-compare
/// logic is already fully proven in <c>GeoAssets.Core.Tests.Services.Evidence.EvidenceVerificationServiceTests</c>;
/// this file only proves the endpoint reaches it and round-trips the result as JSON, plus the
/// default "every endpoint requires an authenticated caller" boundary — no EF/tenant concerns of
/// its own to fake, unlike <see cref="FieldWorkInspectionApiTests"/>.
/// </summary>
public sealed class EvidenceVerificationApiTests
{
    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }

    private static async Task<TestServer> BuildServerAsync()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
                    services.AddAuthorization(options =>
                        options.FallbackPolicy = new AuthorizationPolicyBuilder()
                            .RequireAuthenticatedUser()
                            .Build());
                });
                webHost.Configure(app =>
                {
                    app.Use(async (ctx, next) =>
                    {
                        if (ctx.Request.Headers.ContainsKey("X-Test-Authenticated"))
                            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                                [new Claim(ClaimTypes.NameIdentifier, "test-user")], "TestScheme"));
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapEvidenceVerificationApi());
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    private static byte[] FakeJpeg() => [0xFF, 0xD8, 0x01, 0x02, 0x03, 0xFF, 0xD9];

    private static byte[] BuildSignedJpeg(byte[] rawJpeg, string tenantSecret)
    {
        var capturedAt = new DateTimeOffset(2026, 11, 1, 14, 32, 0, TimeSpan.Zero);
        var signature = EvidenceHmacSigner.ComputeSignature(rawJpeg, -23.55052, -46.633309, capturedAt, "order-1", tenantSecret);
        var descriptor = new EvidenceSignatureDescriptor(signature, -23.55052, -46.633309, capturedAt, "order-1");
        return JpegExifImageDescriptionCodec.WriteImageDescription(rawJpeg, descriptor.ToDescriptorString());
    }

    [Fact]
    public async Task VerifyEvidence_NoAuthenticatedPrincipal_Returns401()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/evidence/verify", UriKind.Relative),
            new VerifyEvidenceRequest(FakeJpeg(), "tenant-secret"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task VerifyEvidence_UntamperedPayload_ReturnsValidTrue()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        var signed = BuildSignedJpeg(FakeJpeg(), "tenant-secret");

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/evidence/verify", UriKind.Relative),
            new VerifyEvidenceRequest(signed, "tenant-secret"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<EvidenceVerificationResult>();
        result!.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyEvidence_TamperedPayload_ReturnsValidFalse()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        var signed = BuildSignedJpeg(FakeJpeg(), "tenant-secret");
        signed[^1] ^= 0xFF;

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/evidence/verify", UriKind.Relative),
            new VerifyEvidenceRequest(signed, "tenant-secret"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<EvidenceVerificationResult>();
        result!.IsValid.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrWhiteSpace();
    }
}

