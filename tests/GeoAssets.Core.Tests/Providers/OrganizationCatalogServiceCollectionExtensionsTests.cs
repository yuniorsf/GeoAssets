using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Core.Tests.Providers;

public sealed class OrganizationCatalogServiceCollectionExtensionsTests
{
    private static readonly object Key = OrganizationCatalogKeys.SharedCatalog;

    private static IAssetProvider Shared(ServiceProvider provider) => provider.GetRequiredKeyedService<IAssetProvider>(Key);

    [Fact]
    public void SharedCatalogKey_IsTheDocumentedValue() =>
        OrganizationCatalogKeys.SharedCatalog.Should().Be("geoassets:shared-asset-catalog");

    [Fact]
    public async Task MoveSharedAssetCatalogToKey_InstanceSingleton_IsResolvableUnderTheKey_AndNoLongerUnkeyed()
    {
        var instance = new TestAssetProvider();
        var services = new ServiceCollection();
        services.AddSingleton<IAssetProvider>(instance);

        services.MoveSharedAssetCatalogToKey();

        await using var provider = services.BuildServiceProvider();
        Shared(provider).Should().BeSameAs(instance);
        provider.GetService<IAssetProvider>().Should().BeNull();
    }

    [Fact]
    public async Task MoveSharedAssetCatalogToKey_FactorySingleton_ResolvesTheFactoryResultOnce()
    {
        var created = 0;
        var services = new ServiceCollection();
        services.AddSingleton<IAssetProvider>(_ =>
        {
            created++;
            return new TestAssetProvider();
        });

        services.MoveSharedAssetCatalogToKey();

        await using var provider = services.BuildServiceProvider();
        Shared(provider).Should().BeSameAs(Shared(provider));
        created.Should().Be(1);
    }

    [Fact]
    public async Task MoveSharedAssetCatalogToKey_TypeSingleton_ResolvesAnInstanceOfThatType()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAssetProvider, TestAssetProvider>();

        services.MoveSharedAssetCatalogToKey();

        await using var provider = services.BuildServiceProvider();
        Shared(provider).Should().BeOfType<TestAssetProvider>().And.BeSameAs(Shared(provider));
    }

    [Fact]
    public async Task MoveSharedAssetCatalogToKey_SeveralUnkeyedRegistrations_RemovesAllAndKeepsTheLast()
    {
        var first = new TestAssetProvider();
        var last = new TestAssetProvider();
        var services = new ServiceCollection();
        services.AddSingleton<IAssetProvider>(first);
        services.AddScoped<IAssetProvider>(_ => new TestAssetProvider());
        services.AddSingleton<IAssetProvider>(last);

        services.MoveSharedAssetCatalogToKey();

        services.Where(d => d.ServiceType == typeof(IAssetProvider) && !d.IsKeyedService).Should().BeEmpty();
        await using var provider = services.BuildServiceProvider();
        Shared(provider).Should().BeSameAs(last);
    }

    [Fact]
    public void MoveSharedAssetCatalogToKey_ReturnsTheCollection()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAssetProvider>(new TestAssetProvider());

        services.MoveSharedAssetCatalogToKey().Should().BeSameAs(services);
    }

    [Fact]
    public void MoveSharedAssetCatalogToKey_NoRegistration_Throws()
    {
        var act = () => new ServiceCollection().MoveSharedAssetCatalogToKey();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MoveSharedAssetCatalogToKey_OnlyAKeyedRegistration_Throws()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IAssetProvider>("other", new TestAssetProvider());

        var act = () => services.MoveSharedAssetCatalogToKey();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MoveSharedAssetCatalogToKey_LastRegistrationScoped_Throws_AndLeavesTheCollectionUntouched()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAssetProvider>(new TestAssetProvider());
        services.AddScoped<IAssetProvider, TestAssetProvider>();

        var act = () => services.MoveSharedAssetCatalogToKey();

        act.Should().Throw<InvalidOperationException>();
        services.Count(d => d.ServiceType == typeof(IAssetProvider)).Should().Be(2);
    }

    [Fact]
    public void MoveSharedAssetCatalogToKey_NullServices_Throws()
    {
        var act = () => OrganizationCatalogServiceCollectionExtensions.MoveSharedAssetCatalogToKey(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
