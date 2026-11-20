using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GeoAssets.Server.Tests.TokenAudiences;

/// <summary>
/// XD01-390: <see cref="ForwardedBearerTokenHandler"/> turns a kernel 401 to a forwarded token into a
/// <see cref="KernelRejectedCallerTokenException"/> carrying the kernel's Bearer challenge and the caller principal's
/// <c>aud</c>/<c>iss</c> — and nothing else (any other answer, or a 401 with no forwarded token, comes back unchanged).
/// </summary>
public sealed class ForwardedBearerTokenHandlerRejectionTests
{
    private const string RawToken = "header.payload-secret-looking.signature";
    private const string KernelUrl = "http://kernel.test/api/identity/me";

    private sealed class RespondingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Seen { get; private set; }

        public HttpResponseMessage? Returned { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request;
            Returned = respond(request);
            return Task.FromResult(Returned);
        }
    }

    private sealed class FixedHttpContextAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private static HttpResponseMessage Unauthorized(params AuthenticationHeaderValue[] challenges)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        foreach (var challenge in challenges) response.Headers.WwwAuthenticate.Add(challenge);
        return response;
    }

    private static DefaultHttpContext Context(string? authorization = "Bearer " + RawToken, IEnumerable<Claim>? claims = null)
    {
        var context = new DefaultHttpContext();
        if (authorization is not null) context.Request.Headers.Authorization = authorization;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims ?? [], "test"));
        return context;
    }

    private static async Task<(HttpResponseMessage? Response, KernelRejectedCallerTokenException? Rejection, RespondingHandler Inner)> SendAsync(
        HttpContext? context, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var inner = new RespondingHandler(respond);
        using var handler = new ForwardedBearerTokenHandler(new FixedHttpContextAccessor(context)) { InnerHandler = inner };
        using var client = new HttpClient(handler);
        try
        {
            return (await client.GetAsync(new Uri(KernelUrl)), null, inner);
        }
        catch (KernelRejectedCallerTokenException ex)
        {
            return (null, ex, inner);
        }
    }

    private static async Task<KernelRejectedCallerTokenException> RejectionOfAsync(HttpContext context, HttpResponseMessage response)
    {
        var (_, rejection, _) = await SendAsync(context, _ => response);
        return rejection.Should().NotBeNull().And.Subject.As<KernelRejectedCallerTokenException>();
    }

    [Fact]
    public async Task ABearerTokenOfTheRequest_IsForwarded_AndASuccessfulAnswerIsReturnedAsItIs()
    {
        var (response, rejection, inner) = await SendAsync(Context(), _ => new HttpResponseMessage(HttpStatusCode.OK));

        rejection.Should().BeNull();
        response!.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.Seen!.Headers.Authorization!.ToString().Should().Be("Bearer " + RawToken);
        response.Dispose();
    }

    [Fact]
    public async Task AKernel401ToTheForwardedToken_Throws_WithTheKernelsErrorAndDescription_ThePath_AndTheTokensAudAndIss()
    {
        var challenge = new AuthenticationHeaderValue("Bearer", "error=\"invalid_token\", error_description=\"The audience 'x' is invalid\"");
        var context = Context(claims: [new Claim("aud", "vertical-api"), new Claim("iss", "https://issuer.test/v2.0")]);

        var rejection = await RejectionOfAsync(context, Unauthorized(challenge));

        rejection.KernelPath.Should().Be("/api/identity/me");
        rejection.KernelError.Should().Be("invalid_token");
        rejection.KernelErrorDescription.Should().Be("The audience 'x' is invalid");
        rejection.TokenAudience.Should().Be("vertical-api");
        rejection.TokenIssuer.Should().Be("https://issuer.test/v2.0");
    }

    [Fact]
    public async Task WhenTheTokenHasSeveralAudiences_TheyAreJoinedWithACommaAndSpace()
    {
        var context = Context(claims: [new Claim("aud", "first-api"), new Claim("aud", "second-api"), new Claim("aud", " ")]);

        var rejection = await RejectionOfAsync(context, Unauthorized());

        rejection.TokenAudience.Should().Be("first-api, second-api");
    }

    [Fact]
    public async Task WhenThePrincipalCarriesNoAudOrIss_TheyAreNull()
    {
        var rejection = await RejectionOfAsync(Context(), Unauthorized());

        rejection.TokenAudience.Should().BeNull();
        rejection.TokenIssuer.Should().BeNull();
    }

    [Fact]
    public async Task ANoChallengeAtAll_StillThrows_WithoutAnErrorOrDescription()
    {
        var rejection = await RejectionOfAsync(Context(), Unauthorized());

        rejection.KernelError.Should().BeNull();
        rejection.KernelErrorDescription.Should().BeNull();
    }

    [Fact]
    public async Task ABearerChallengeWithoutParameters_HasNoErrorOrDescription()
    {
        var rejection = await RejectionOfAsync(Context(), Unauthorized(new AuthenticationHeaderValue("Bearer")));

        rejection.KernelError.Should().BeNull();
        rejection.KernelErrorDescription.Should().BeNull();
    }

    [Fact]
    public async Task QuotedValues_MayHoldCommasAndEscapedQuotes()
    {
        var challenge = new AuthenticationHeaderValue(
            "Bearer", "realm=\"kernel\", error=\"invalid_token\", error_description=\"The audience 'a, b' is \\\"invalid\\\", sorry\"");

        var rejection = await RejectionOfAsync(Context(), Unauthorized(challenge));

        rejection.KernelError.Should().Be("invalid_token");
        rejection.KernelErrorDescription.Should().Be("The audience 'a, b' is \"invalid\", sorry");
    }

    [Fact]
    public async Task UnquotedValues_AreReadUpToTheNextComma()
    {
        var challenge = new AuthenticationHeaderValue("Bearer", "error=invalid_token, error_description=expired");

        var rejection = await RejectionOfAsync(Context(), Unauthorized(challenge));

        rejection.KernelError.Should().Be("invalid_token");
        rejection.KernelErrorDescription.Should().Be("expired");
    }

    [Fact]
    public async Task TheBearerSchemeInLowerCase_IsRecognised_AndParameterNamesInAnyCase()
    {
        var challenge = new AuthenticationHeaderValue("bearer", "Error=\"invalid_token\", ERROR_DESCRIPTION=\"nope\"");

        var rejection = await RejectionOfAsync(Context(), Unauthorized(challenge));

        rejection.KernelError.Should().Be("invalid_token");
        rejection.KernelErrorDescription.Should().Be("nope");
    }

    [Fact]
    public async Task ChallengesOfOtherSchemes_AreIgnored_WhenTheyComeBeforeTheBearerOne()
    {
        var response = Unauthorized(
            new AuthenticationHeaderValue("Basic", "realm=\"x\", error=\"basic-error\""),
            new AuthenticationHeaderValue("Bearer", "error=\"invalid_token\""));

        var rejection = await RejectionOfAsync(Context(), response);

        rejection.KernelError.Should().Be("invalid_token");
        rejection.KernelErrorDescription.Should().BeNull();
    }

    [Fact]
    public async Task OnlyAChallengeOfAnotherScheme_YieldsNoError()
    {
        var rejection = await RejectionOfAsync(Context(), Unauthorized(new AuthenticationHeaderValue("Basic", "error=\"x\"")));

        rejection.KernelError.Should().BeNull();
    }

    [Fact]
    public async Task AParameterWithoutAnEqualsSign_IsSkipped()
    {
        var challenge = new AuthenticationHeaderValue("Bearer", "junk, error=\"invalid_token\"");

        var rejection = await RejectionOfAsync(Context(), Unauthorized(challenge));

        rejection.KernelError.Should().Be("invalid_token");
    }

    [Fact]
    public async Task TheFirstOccurrenceOfAParameter_Wins()
    {
        var challenge = new AuthenticationHeaderValue("Bearer", "error=\"first\", error=\"second\"");

        var rejection = await RejectionOfAsync(Context(), Unauthorized(challenge));

        rejection.KernelError.Should().Be("first");
    }

    [Fact]
    public async Task AKernel401_WhenNoTokenWasForwarded_IsReturnedUnchanged()
    {
        var (response, rejection, _) = await SendAsync(Context(authorization: null), _ => Unauthorized());

        rejection.Should().BeNull();
        response!.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Dispose();
    }

    [Fact]
    public async Task AKernel401_ForANonBearerAuthorization_IsReturnedUnchanged()
    {
        var (response, rejection, _) = await SendAsync(Context(authorization: "Basic dXNlcjpwYXNz"), _ => Unauthorized());

        rejection.Should().BeNull();
        response!.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Dispose();
    }

    [Fact]
    public async Task AKernel401_OutsideARequest_IsReturnedUnchanged()
    {
        var (response, rejection, _) = await SendAsync(context: null, _ => Unauthorized());

        rejection.Should().BeNull();
        response!.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Dispose();
    }

    [Fact]
    public async Task AKernel403_IsReturnedUnchanged()
    {
        var (response, rejection, _) = await SendAsync(Context(), _ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        rejection.Should().BeNull();
        response!.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Dispose();
    }

    [Fact]
    public async Task AKernel401_ToAForwardedToken_DisposesTheResponseItDiscards()
    {
        var (_, _, inner) = await SendAsync(Context(), _ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new TrackingContent() });

        inner.Returned!.Content.As<TrackingContent>().Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task TheExceptionsMessageAndToString_NeverContainTheRawToken()
    {
        var challenge = new AuthenticationHeaderValue("Bearer", "error=\"invalid_token\", error_description=\"The audience is invalid\"");
        var context = Context(claims: [new Claim("aud", "vertical-api"), new Claim("iss", "https://issuer.test/v2.0")]);

        var rejection = await RejectionOfAsync(context, Unauthorized(challenge));

        rejection.Message.Should().NotContain(RawToken).And.Contain("/api/identity/me").And.Contain("vertical-api");
        rejection.ToString().Should().NotContain(RawToken);
    }

    [Fact]
    public void TheMessageOnlyConstructors_KeepTheirMessageAndLeaveTheDiagnosticsEmpty()
    {
        var inner = new InvalidOperationException("inner");

        new KernelRejectedCallerTokenException().Message.Should().NotBeNullOrWhiteSpace();
        new KernelRejectedCallerTokenException("custom").Message.Should().Be("custom");
        var withInner = new KernelRejectedCallerTokenException("custom", inner);
        withInner.InnerException.Should().BeSameAs(inner);
        withInner.KernelPath.Should().BeNull();
        withInner.TokenAudience.Should().BeNull();
    }

    [Fact]
    public void TheDiagnosticConstructor_WithNothingKnown_DescribesTheMissingValues()
    {
        var rejection = new KernelRejectedCallerTokenException(null, null, null, null, null);

        rejection.Message.Should().Contain("(unknown route)").And.Contain("(none)").And.Contain("(no description)");
    }

    private sealed class TrackingContent : HttpContent
    {
        public bool Disposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
