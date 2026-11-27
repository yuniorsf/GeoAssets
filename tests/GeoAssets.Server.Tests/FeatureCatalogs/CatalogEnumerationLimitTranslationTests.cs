using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.FeatureCatalogs;

/// <summary>XD01-449: <see cref="CatalogEnumerationLimitTranslationExtensions"/> — a read too large for a PostGIS-served organization catalog.</summary>
public sealed class CatalogEnumerationLimitTranslationTests
{
    private static async Task<(int Status, string ContentType, string Body)> RunAsync(RequestDelegate endpoint)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseCatalogEnumerationLimitTranslation();
        app.Run(endpoint);

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = "/api/geoassets/features";
        context.Response.Body = new MemoryStream();

        await app.Build()(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return (context.Response.StatusCode, context.Response.ContentType ?? string.Empty, await reader.ReadToEndAsync());
    }

    [Fact]
    public void TheProblemCode_IsTheDocumentedOne() =>
        CatalogEnumerationLimitTranslationExtensions.ProblemCode.Should().Be("catalog.enumeration-limit");

    [Fact]
    public async Task ARefusedEnumeration_Answers422WithTheCodeTheOperationAndTheLimit()
    {
        var (status, contentType, body) = await RunAsync(_ => throw new CatalogEnumerationLimitException("GetAll", 10_000));

        status.Should().Be(StatusCodes.Status422UnprocessableEntity);
        contentType.Should().StartWith("application/problem+json");
        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;
        problem.GetProperty("code").GetString().Should().Be("catalog.enumeration-limit");
        problem.GetProperty("operation").GetString().Should().Be("GetAll");
        problem.GetProperty("limit").GetInt32().Should().Be(10_000);
        problem.GetProperty("title").GetString().Should().Be("Too many features for this read");
        problem.GetProperty("detail").GetString().Should().Contain("GetAll").And.Contain("10000");
    }

    [Fact]
    public async Task ASuccessfulRequest_PassesThroughUnchanged()
    {
        var (status, _, body) = await RunAsync(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsync("ok");
        });

        status.Should().Be(StatusCodes.Status200OK);
        body.Should().Be("ok");
    }

    [Fact]
    public Task AnyOtherException_IsNotTranslated()
    {
        var act = () => RunAsync(_ => throw new InvalidOperationException("boom"));

        return act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }
}
