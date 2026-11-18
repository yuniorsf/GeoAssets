using Xunit;
using FluentAssertions;
using GeoAssets.Hosting.Abstractions;

namespace GeoAssets.Hosting.Abstractions.Tests;

public sealed class GeoAssetsModuleResolverTests
{
    [Fact]
    public void Resolve_ReturnsOnlyEnabledModules_InConfiguredOrder()
    {
        var a = new RecordingModule("a");
        var b = new RecordingModule("b");
        var c = new RecordingModule("c");

        var result = GeoAssetsModuleResolver.Resolve([a, b, c], ["c", "a"]);

        result.Should().Equal(c, a);
    }

    [Fact]
    public void Resolve_NoEnabledIds_ReturnsNone()
    {
        GeoAssetsModuleResolver.Resolve([new RecordingModule("a")], []).Should().BeEmpty();
    }

    [Fact]
    public void Resolve_UnknownId_ThrowsNamingTheId()
    {
        var act = () => GeoAssetsModuleResolver.Resolve([new RecordingModule("a")], ["ghost"]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ghost*");
    }

    [Fact]
    public void Resolve_DuplicateEnabledId_ThrowsNamingTheId()
    {
        var act = () => GeoAssetsModuleResolver.Resolve([new RecordingModule("a")], ["a", "a"]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'a'*");
    }

    [Fact]
    public void Resolve_DuplicateBuiltInId_ThrowsNamingTheId()
    {
        var act = () => GeoAssetsModuleResolver.Resolve([new RecordingModule("dup"), new RecordingModule("dup")], ["dup"]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*dup*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_EmptyBuiltInId_Throws(string id)
    {
        var act = () => GeoAssetsModuleResolver.Resolve([new RecordingModule(id)], []);

        act.Should().Throw<InvalidOperationException>().WithMessage("*empty Id*");
    }

    [Fact]
    public void Resolve_NullArguments_Throw()
    {
        var nullBuiltIns = () => GeoAssetsModuleResolver.Resolve(null!, []);
        var nullIds = () => GeoAssetsModuleResolver.Resolve([], null!);
        var nullModule = () => GeoAssetsModuleResolver.Resolve([null!], []);

        nullBuiltIns.Should().Throw<ArgumentNullException>();
        nullIds.Should().Throw<ArgumentNullException>();
        nullModule.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Catalog_NullModules_Throws()
    {
        var act = () => new GeoAssetsModuleCatalog(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
