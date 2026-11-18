using Xunit;
using FluentAssertions;
using GeoAssets.Hosting.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace GeoAssets.Hosting.Abstractions.Tests;

public sealed class ModuleRouteCollisionDetectorTests
{
    private static Endpoint Map(string template, params string[] methods)
    {
        var builder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse(template), 0);
        if (methods.Length > 0)
        {
            builder.Metadata.Add(new HttpMethodMetadata(methods));
        }

        return builder.Build();
    }

    private static IReadOnlyList<(string ModuleId, IReadOnlyList<Endpoint> Endpoints)> Modules(
        (string Id, Endpoint[] Endpoints) first, (string Id, Endpoint[] Endpoints) second) =>
        [(first.Id, first.Endpoints), (second.Id, second.Endpoints)];

    [Fact]
    public void SameTemplateAndMethod_InTwoModules_ThrowsNamingBothModules()
    {
        var act = () => ModuleRouteCollisionDetector.ThrowIfCollides(
            Modules(("alpha", [Map("/x/{id}", "GET")]), ("beta", [Map("/x/{id}", "GET")])));

        act.Should().Throw<InvalidOperationException>().WithMessage("*alpha*beta*GET*");
    }

    [Fact]
    public void TemplatesDifferingOnlyInParameterNamesAndCase_Collide()
    {
        var act = () => ModuleRouteCollisionDetector.ThrowIfCollides(
            Modules(("alpha", [Map("/Orders/{id}", "GET")]), ("beta", [Map("/orders/{orderId:guid}", "get")])));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SameTemplate_DisjointMethods_DoesNotThrow()
    {
        var act = () => ModuleRouteCollisionDetector.ThrowIfCollides(
            Modules(("alpha", [Map("/x", "GET")]), ("beta", [Map("/x", "POST")])));

        act.Should().NotThrow();
    }

    [Fact]
    public void EndpointWithoutMethodMetadata_CollidesWithAnyMethod()
    {
        var first = () => ModuleRouteCollisionDetector.ThrowIfCollides(
            Modules(("alpha", [Map("/x")]), ("beta", [Map("/x", "GET")])));
        var second = () => ModuleRouteCollisionDetector.ThrowIfCollides(
            Modules(("alpha", [Map("/x", "GET")]), ("beta", [Map("/x")])));
        var emptyMethods = () => ModuleRouteCollisionDetector.ThrowIfCollides(
            Modules(("alpha", [Map("/x", [])]), ("beta", [Map("/x")])));

        first.Should().Throw<InvalidOperationException>().WithMessage("*alpha*beta*'/X'.");
        second.Should().Throw<InvalidOperationException>();
        emptyMethods.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SameTemplate_WithinOneModule_DoesNotThrow()
    {
        var act = () => ModuleRouteCollisionDetector.ThrowIfCollides(
            [("alpha", [Map("/x", "GET"), Map("/x", "GET")]), ("beta", [Map("/y", "GET")])]);

        act.Should().NotThrow();
    }

    [Fact]
    public void DifferentTemplates_DoNotThrow()
    {
        var act = () => ModuleRouteCollisionDetector.ThrowIfCollides(
            Modules(("alpha", [Map("/x/{id}", "GET")]), ("beta", [Map("/x/{id}/y", "GET"), Map("/z", "GET")])));

        act.Should().NotThrow();
    }

    [Fact]
    public void CatchAllAndPlainParameter_DoNotCollide()
    {
        var act = () => ModuleRouteCollisionDetector.ThrowIfCollides(
            Modules(("alpha", [Map("/files/{*path}", "GET")]), ("beta", [Map("/files/{id}", "GET")])));
        var catchAllBoth = () => ModuleRouteCollisionDetector.ThrowIfCollides(
            Modules(("alpha", [Map("/files/{*path}", "GET")]), ("beta", [Map("/files/{*rest}", "GET")])));

        act.Should().NotThrow();
        catchAllBoth.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void NonRouteEndpoints_AreIgnored()
    {
        var plain = new Endpoint(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, "plain");

        var act = () => ModuleRouteCollisionDetector.ThrowIfCollides(
            Modules(("alpha", [plain]), ("beta", [plain])));

        act.Should().NotThrow();
    }

    [Fact]
    public void Templates_WithSeparatorParts_AreNormalized()
    {
        var act = () => ModuleRouteCollisionDetector.ThrowIfCollides(
            Modules(("alpha", [Map("/f/{name}.{ext}", "GET")]), ("beta", [Map("/F/{a}.{b}", "GET")])));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void OptionalParameterSeparatorTemplates_Collide()
    {
        var act = () => ModuleRouteCollisionDetector.ThrowIfCollides(
            Modules(("alpha", [Map("/files/{name}.{ext?}", "GET")]), ("beta", [Map("/FILES/{n}.{e?}", "GET")])));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void NullArgument_Throws()
    {
        var act = () => ModuleRouteCollisionDetector.ThrowIfCollides(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
