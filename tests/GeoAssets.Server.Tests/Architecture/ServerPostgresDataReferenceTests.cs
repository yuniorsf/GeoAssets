using System.Reflection;
using FluentAssertions;
using GeoAssets.Server;
using Xunit;

namespace GeoAssets.Server.Tests.Architecture;

/// <summary>
/// The kernel names no PostgreSQL provider internals (XD01-372, ADR-001 Phase 4): the host reaches the database only
/// through the provider's registration extensions, so no source file and no type of <c>GeoAssets.Server</c> refers to
/// <c>GeoAssets.Provider.PostgreSQL.Data</c> (the DbContext, its rows and its factory).
/// </summary>
public sealed class ServerPostgresDataReferenceTests
{
    private const string ForbiddenNamespace = "GeoAssets.Provider.PostgreSQL.Data";

    private static readonly string[] SkippedDirectories = ["bin", "obj"];

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("GeoAssets.sln was not found above the test output directory.");
    }

    private static bool InNamespace(Type? type) =>
        type is not null
        && (type.Namespace is { } ns
            && (string.Equals(ns, ForbiddenNamespace, StringComparison.Ordinal)
                || ns.StartsWith(ForbiddenNamespace + ".", StringComparison.Ordinal))
            || InNamespace(type.HasElementType ? type.GetElementType() : null)
            || type.IsGenericType && type.GetGenericArguments().Any(InNamespace));

    [Fact]
    public void Server_DoesNotReferenceProviderPostgreSqlData()
    {
        var serverRoot = Path.Combine(RepositoryRoot(), "apps", "GeoAssets.Server");
        var sources = Directory.EnumerateFiles(serverRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(serverRoot, path)
                .Split(Path.DirectorySeparatorChar)
                .Any(segment => SkippedDirectories.Contains(segment, StringComparer.Ordinal)))
            .ToArray();

        sources.Should().NotBeEmpty("the scan must actually find the Server sources");
        var offenders = sources
            .Where(path => File.ReadAllText(path).Contains(ForbiddenNamespace, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(serverRoot, path))
            .Order(StringComparer.Ordinal);

        offenders.Should().BeEmpty("the kernel must not name {0}", ForbiddenNamespace);
    }

    [Fact]
    public void Server_TypesDoNotExposeOrDeriveFromProviderPostgreSqlData()
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var serverAssembly = typeof(WmsEndpointExtensions).Assembly;

        var offenders = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in serverAssembly.GetTypes())
        {
            if (InNamespace(type.BaseType) || type.GetInterfaces().Any(InNamespace))
                offenders.Add($"{type.FullName} (base type)");

            foreach (var field in type.GetFields(All).Where(f => InNamespace(f.FieldType)))
                offenders.Add($"{type.FullName}.{field.Name} (field)");
            foreach (var property in type.GetProperties(All).Where(p => InNamespace(p.PropertyType)))
                offenders.Add($"{type.FullName}.{property.Name} (property)");
            foreach (var method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
            {
                if (method is MethodInfo { ReturnType: var returnType } && InNamespace(returnType))
                    offenders.Add($"{type.FullName}.{method.Name} (return)");
                if (method.GetParameters().Any(p => InNamespace(p.ParameterType)))
                    offenders.Add($"{type.FullName}.{method.Name} (parameter)");
            }
        }

        offenders.Should().BeEmpty("no Server type may use {0}", ForbiddenNamespace);
    }
}
