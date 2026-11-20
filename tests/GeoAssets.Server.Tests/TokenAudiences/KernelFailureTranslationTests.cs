using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GeoAssets.Server.Tests.TokenAudiences;

/// <summary>XD01-390: <see cref="KernelFailureTranslation"/> — what a failed call to the kernel becomes on a vertical host.</summary>
public sealed class KernelFailureTranslationTests
{
    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = 200;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = new MemoryStream();

        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
    }

    private static (RequestDelegate Pipeline, IServiceProvider Services, CapturingLoggerProvider Logs) Build(Exception toThrow, CapturingLoggerProvider? existing = null)
    {
        var logs = existing ?? new CapturingLoggerProvider();
        var services = new ServiceCollection()
            .AddLogging(b => b.AddProvider(logs))
            .BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseKernelFailureTranslation();
        app.Run(_ => throw toThrow);
        return (app.Build(), services, logs);
    }

    private static async Task<(int Status, JsonElement Body, string ContentType)> RunAsync(Exception toThrow, CapturingLoggerProvider? logsOut = null)
    {
        var (pipeline, services, _) = Build(toThrow, logsOut);
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = "/api/ip-billing/municipio-grants/sync";
        context.Response.Body = new MemoryStream();

        await pipeline(context);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, document.RootElement.Clone(), context.Response.ContentType ?? string.Empty);
    }

    [Fact]
    public void TheProblemCodes_AreTheDocumentedOnes()
    {
        KernelFailureTranslation.UnavailableProblemCode.Should().Be("kernel.unavailable");
        KernelFailureTranslation.RejectedProblemCode.Should().Be("kernel.rejected");
        KernelFailureTranslation.TokenRejectedProblemCode.Should().Be("kernel.token-rejected");
        KernelFailureTranslation.ConflictProblemCode.Should().Be("kernel.conflict");
    }

    [Fact]
    public async Task AKernelRejectedCallerToken_Answers502TokenRejected_NamingTheAdditionalAudiencesKey()
    {
        var exception = new KernelRejectedCallerTokenException("/api/identity/me", "invalid_token", "The audience is invalid", "vertical-api", "https://issuer.test");

        var (status, body, contentType) = await RunAsync(exception);

        status.Should().Be(StatusCodes.Status502BadGateway);
        contentType.Should().StartWith("application/problem+json");
        body.GetProperty("code").GetString().Should().Be("kernel.token-rejected");
        body.GetProperty("title").GetString().Should().Be("The GeoAssets kernel rejected the caller's token.");
        body.GetProperty("detail").GetString().Should().Contain("AzureAdCiam:AdditionalAudiences").And.EndWith("The kernel said: The audience is invalid");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AKernelRejectedCallerToken_WithoutADescription_HasNoKernelSaidSuffix(string? description)
    {
        var exception = new KernelRejectedCallerTokenException("/api/identity/me", "invalid_token", description, "vertical-api", null);

        var (_, body, _) = await RunAsync(exception);

        body.GetProperty("detail").GetString().Should().Contain("AzureAdCiam:AdditionalAudiences").And.NotContain("The kernel said");
    }

    [Fact]
    public async Task AKernelRejectedCallerToken_IsLoggedOnceAtError_WithTheDiagnosis_AndNeverATokenValue()
    {
        var logs = new CapturingLoggerProvider();
        var exception = new KernelRejectedCallerTokenException("/api/identity/me", "invalid_token", "The audience is invalid", "vertical-api", "https://issuer.test");

        await RunAsync(exception, logs);

        var entry = logs.Entries.Where(e => e.Level >= LogLevel.Warning).Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Message.Should().Contain("vertical-api").And.Contain("https://issuer.test").And.Contain("/api/identity/me")
            .And.Contain("invalid_token").And.Contain("AzureAdCiam:AdditionalAudiences");
    }

    [Fact]
    public async Task AnOrganizationGrantConflict_Answers409KernelConflict_LoggedAtWarning()
    {
        var logs = new CapturingLoggerProvider();
        var exception = new GeoAssets.Workflow.Rest.OrganizationGrantConflictException(
            new GeoAssets.Identity.Authorization.Services.OrganizationGrantKey(Guid.NewGuid(), Guid.NewGuid(), null, "3550308"), "kernel detail");

        var (status, body, contentType) = await RunAsync(exception, logs);

        status.Should().Be(StatusCodes.Status409Conflict);
        contentType.Should().StartWith("application/problem+json");
        body.GetProperty("code").GetString().Should().Be("kernel.conflict");
        body.GetProperty("title").GetString().Should().Be("The kernel refused a concurrent write.");
        body.GetProperty("detail").GetString().Should().Be("Concurrent writes of the same organization grant raced this request; repeat it.");
        logs.Entries.Where(e => e.Level >= LogLevel.Warning).Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public Task WhenTheResponseHasAlreadyStarted_AnOrganizationGrantConflict_PassesThrough()
    {
        var (pipeline, services, _) = Build(new GeoAssets.Workflow.Rest.OrganizationGrantConflictException(
            new GeoAssets.Identity.Authorization.Services.OrganizationGrantKey(Guid.NewGuid(), Guid.NewGuid(), null, null), null));
        var context = new DefaultHttpContext { RequestServices = services };
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        var act = async () => await pipeline(context);

        return act.Should().ThrowAsync<GeoAssets.Workflow.Rest.OrganizationGrantConflictException>();
    }

    [Fact]
    public async Task AnHttpRequestExceptionWith400_Answers400KernelRejected_WithItsMessage()
    {
        var (status, body, _) = await RunAsync(new HttpRequestException("The kernel said no.", null, HttpStatusCode.BadRequest));

        status.Should().Be(StatusCodes.Status400BadRequest);
        body.GetProperty("code").GetString().Should().Be("kernel.rejected");
        body.GetProperty("detail").GetString().Should().Be("The kernel said no.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task AnyOtherHttpRequestException_Answers503KernelUnavailable(HttpStatusCode? kernelStatus)
    {
        var logs = new CapturingLoggerProvider();

        var (status, body, _) = await RunAsync(new HttpRequestException("boom", null, kernelStatus), logs);

        status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        body.GetProperty("code").GetString().Should().Be("kernel.unavailable");
        logs.Entries.Where(e => e.Level >= LogLevel.Warning).Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public Task AnExceptionThatIsNotAKernelFailure_PassesThroughUntouched()
    {
        var (pipeline, services, _) = Build(new InvalidOperationException("not the kernel"));
        var context = new DefaultHttpContext { RequestServices = services };

        var act = async () => await pipeline(context);

        return act.Should().ThrowAsync<InvalidOperationException>().WithMessage("not the kernel");
    }

    [Fact]
    public Task WhenTheResponseHasAlreadyStarted_ATokenRejection_PassesThrough()
    {
        var (pipeline, services, _) = Build(new KernelRejectedCallerTokenException("/api/identity/me", null, null, null, null));
        var context = new DefaultHttpContext { RequestServices = services };
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        var act = async () => await pipeline(context);

        return act.Should().ThrowAsync<KernelRejectedCallerTokenException>();
    }

    [Fact]
    public Task WhenTheResponseHasAlreadyStarted_AnHttpRequestException_PassesThrough()
    {
        var (pipeline, services, _) = Build(new HttpRequestException("late", null, HttpStatusCode.ServiceUnavailable));
        var context = new DefaultHttpContext { RequestServices = services };
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        var act = async () => await pipeline(context);

        return act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ARequestThatSucceeds_IsNotTouched()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseKernelFailureTranslation();
        app.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext { RequestServices = services };

        await app.Build()(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    [Fact]
    public void UseKernelFailureTranslation_WithoutAnApplicationBuilder_Throws()
    {
        var act = () => KernelFailureTranslation.UseKernelFailureTranslation(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
