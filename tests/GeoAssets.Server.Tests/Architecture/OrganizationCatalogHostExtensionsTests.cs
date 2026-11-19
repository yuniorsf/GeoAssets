using System.Reflection;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Server.Tests.Architecture;

[Trait("Category", "Architecture")]
public sealed class OrganizationCatalogHostExtensionsTests
{
    private const string ProviderPrefix = "GeoAssets.Provider.";

    private static string SourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the test runs from inside the repository");

        return Path.Combine(directory!.FullName, "apps", "GeoAssets.Server", "OrganizationAssetCatalogHostExtensions.cs");
    }

    private static IEnumerable<Type> SignatureTypes(MethodInfo method) =>
        method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType);

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        if (type.HasElementType) foreach (var inner in Flatten(type.GetElementType()!)) yield return inner;
        if (type.IsGenericType) foreach (var arg in type.GetGenericArguments()) foreach (var inner in Flatten(arg)) yield return inner;
    }

    [Fact]
    public void OrganizationAssetCatalogHostExtensions_HasNoProviderUsing()
    {
        var lines = File.ReadAllLines(SourcePath());

        lines.Where(line => line.TrimStart().StartsWith($"using {ProviderPrefix}", StringComparison.Ordinal))
            .Should().BeEmpty("the kernel's organization-catalog wiring goes through the IOrganizationCatalogFactory port, not a provider");
    }

    [Fact]
    public void OrganizationAssetCatalogHostExtensions_PublicSignaturesReferenceNoProviderAssemblyType()
    {
        var methods = typeof(OrganizationAssetCatalogHostExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        methods.Should().NotBeEmpty();
        var offending = methods
            .SelectMany(m => SignatureTypes(m).SelectMany(Flatten).Select(t => (Method: m.Name, Type: t)))
            .Where(x => (x.Type.Assembly.GetName().Name ?? string.Empty).StartsWith(ProviderPrefix, StringComparison.Ordinal))
            .ToList();

        offending.Should().BeEmpty();
    }
}
