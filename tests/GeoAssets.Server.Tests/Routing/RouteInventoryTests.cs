using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Xunit;

namespace GeoAssets.Server.Tests.Routing;

[Trait("Category", "RouteInventory")]
public sealed class RouteInventoryTests
{
    private sealed class TestRequirement : IAuthorizationRequirement
    {
    }

    private static RouteEndpoint BuildEndpoint(string pattern, string[]? methods, params object[] metadata)
    {
        var builder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), order: 0);
        if (methods is not null)
        {
            builder.Metadata.Add(new HttpMethodMetadata(methods));
        }

        foreach (var item in metadata)
        {
            builder.Metadata.Add(item);
        }

        return (RouteEndpoint)builder.Build();
    }

    private static string DescribeOne(RouteEndpoint endpoint) => RouteInventory.Describe([endpoint]).Should().ContainSingle().Subject;

    [Fact]
    public void Describe_FormatsMethodsPatternAndPolicy()
    {
        var endpoint = BuildEndpoint("/api/x", ["GET"], new AuthorizeAttribute("SomePolicy"));

        DescribeOne(endpoint).Should().Be("GET /api/x | policy:SomePolicy");
    }

    [Fact]
    public void Describe_MultipleMethods_AreSortedAndJoined()
    {
        var endpoint = BuildEndpoint("/api/x", ["POST", "get"], new AuthorizeAttribute("SomePolicy"));

        DescribeOne(endpoint).Should().StartWith("GET,POST /api/x");
    }

    [Fact]
    public void Describe_NoMethodMetadata_UsesStar()
    {
        var endpoint = BuildEndpoint("/api/x", null);

        DescribeOne(endpoint).Should().StartWith("* /api/x");
    }

    [Fact]
    public void Describe_PatternWithoutLeadingSlash_IsNormalized()
    {
        var withoutSlash = BuildEndpoint("api/y", ["GET"]);
        var empty = BuildEndpoint("", ["GET"]);

        DescribeOne(withoutSlash).Should().StartWith("GET /api/y");
        DescribeOne(empty).Should().StartWith("GET / ");
    }

    [Fact]
    public void Describe_AllowAnonymous_IsReported()
    {
        var endpoint = BuildEndpoint("/api/x", ["GET"], new AllowAnonymousAttribute());

        DescribeOne(endpoint).Should().Be("GET /api/x | anonymous");
    }

    [Fact]
    public void Describe_AuthorizeWithoutPolicy_IsReportedAsAuthorize()
    {
        var endpoint = BuildEndpoint("/api/x", ["GET"], new AuthorizeAttribute());

        DescribeOne(endpoint).Should().Be("GET /api/x | authorize");
    }

    [Fact]
    public void Describe_Roles_AreReported()
    {
        var endpoint = BuildEndpoint("/api/x", ["GET"], new AuthorizeAttribute { Roles = "Admin" });

        DescribeOne(endpoint).Should().Contain("roles:Admin");
    }

    [Fact]
    public void Describe_AuthorizationPolicyRequirements_AreReported()
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new TestRequirement())
            .Build();
        var endpoint = BuildEndpoint("/api/x", ["GET"], policy);

        var line = DescribeOne(endpoint);

        line.Should().Contain("requirement:TestRequirement");
        line.Should().Contain("requirement:DenyAnonymousAuthorizationRequirement");
    }

    [Fact]
    public void Describe_NoAuthorizationMetadata_IsNone()
    {
        var endpoint = BuildEndpoint("/api/x", ["GET"]);

        DescribeOne(endpoint).Should().Be("GET /api/x | none");
    }

    [Fact]
    public void Describe_NonRouteEndpoints_AreIgnored()
    {
        var plain = new Endpoint(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, "plain");
        var route = BuildEndpoint("/api/x", ["GET"]);

        var lines = RouteInventory.Describe([plain, route]);

        lines.Should().ContainSingle().Which.Should().StartWith("GET /api/x");
    }

    [Fact]
    public void Describe_LinesAreSortedOrdinal()
    {
        var b = BuildEndpoint("/b", ["GET"]);
        var upper = BuildEndpoint("/A", ["GET"]);
        var a = BuildEndpoint("/a", ["GET"]);

        var lines = RouteInventory.Describe([b, a, upper]);

        lines.Should().Equal(
            "GET /A | none",
            "GET /a | none",
            "GET /b | none");
    }

    [Fact]
    public void Compare_IdenticalInventories_IsEmpty()
    {
        string[] lines = ["GET /a | none", "POST /b | anonymous"];

        RouteInventory.Compare(lines, lines).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Compare_AddedRoute_IsReportedAsAdded()
    {
        var diff = RouteInventory.Compare(["GET /a | none"], ["GET /a | none", "GET /b | none"]);

        diff.IsEmpty.Should().BeFalse();
        diff.Added.Should().Equal("GET /b | none");
        diff.Removed.Should().BeEmpty();
    }

    [Fact]
    public void Compare_RemovedRoute_IsReportedAsRemoved()
    {
        var diff = RouteInventory.Compare(["GET /a | none", "GET /b | none"], ["GET /a | none"]);

        diff.IsEmpty.Should().BeFalse();
        diff.Removed.Should().Equal("GET /b | none");
        diff.Added.Should().BeEmpty();
    }

    [Fact]
    public void Compare_ChangedPolicy_IsReportedAsRemovedAndAdded()
    {
        var diff = RouteInventory.Compare(["GET /a | policy:Old"], ["GET /a | policy:New"]);

        diff.IsEmpty.Should().BeFalse();
        diff.Removed.Should().Equal("GET /a | policy:Old");
        diff.Added.Should().Equal("GET /a | policy:New");
    }

    [Fact]
    public void Compare_DuplicateLines_AreComparedAsMultiset()
    {
        var diff = RouteInventory.Compare(["GET /a | none", "GET /a | none"], ["GET /a | none"]);

        diff.Removed.Should().Equal("GET /a | none");
        diff.Added.Should().BeEmpty();

        var reverse = RouteInventory.Compare(["GET /a | none"], ["GET /a | none", "GET /a | none"]);

        reverse.Added.Should().Equal("GET /a | none");
        reverse.Removed.Should().BeEmpty();
    }

    [Fact]
    public void Format_ListsRemovedThenAddedWithMarkers()
    {
        var diff = new RouteInventoryDiff(["GET /new | none"], ["GET /old | none"]);

        diff.Format().Should().Be(
            "- GET /old | none" + Environment.NewLine + "+ GET /new | none");
    }

    [Fact]
    public void Format_EmptyDiff_IsEmptyString()
    {
        new RouteInventoryDiff([], []).Format().Should().BeEmpty();
    }
}
