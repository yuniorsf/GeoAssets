using FluentAssertions;
using GeoAssets.Commands.Generation;
using Xunit;

namespace GeoAssets.Commands.Tests.Generation;

public class GeoCommandPluginScaffolderTests
{
    [Fact]
    public void Generate_CreatesExpectedAssemblyAndFiles()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var commandsProjectPath = Path.Combine(tempRoot, "core", "GeoAssets.Commands", "GeoAssets.Commands.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(commandsProjectPath)!);
            File.WriteAllText(commandsProjectPath, "<Project />");

            var outputRoot = Path.Combine(tempRoot, "generated");
            var scaffolder = new GeoCommandPluginScaffolder(commandsProjectPath);

            var generated = scaffolder.Generate(CreateSpec(), outputRoot);

            generated.AssemblyName.Should().Be("GeoAssets.Plugin.AssetTypeSummary");
            generated.Namespace.Should().Be("GeoAssets.Plugin.AssetTypeSummary");
            generated.Files.Select(file => file.RelativePath).Should().BeEquivalentTo(
            [
                "GeoAssets.Plugin.AssetTypeSummary.csproj",
                "AssetTypeSummaryHandler.cs",
                "README.txt",
            ]);

            generated.Files.Single(file => file.RelativePath.EndsWith(".cs", StringComparison.Ordinal)).Content
                .Should().Contain("[ExportGeoCommand(\"asset-type-summary\"")
                .And.Contain("public sealed class AssetTypeSummaryHandler")
                .And.Contain("return GeoCommandResult.Ok(new");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task WriteToDirectoryAsync_WritesProjectFilesToDisk()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var commandsProjectPath = Path.Combine(tempRoot, "core", "GeoAssets.Commands", "GeoAssets.Commands.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(commandsProjectPath)!);
            File.WriteAllText(commandsProjectPath, "<Project />");

            var outputRoot = Path.Combine(tempRoot, "generated");
            var scaffolder = new GeoCommandPluginScaffolder(commandsProjectPath);

            var generated = await scaffolder.WriteToDirectoryAsync(CreateSpec(), outputRoot);

            Directory.Exists(generated.ProjectDirectory).Should().BeTrue();
            File.Exists(Path.Combine(generated.ProjectDirectory, "GeoAssets.Plugin.AssetTypeSummary.csproj")).Should().BeTrue();
            File.Exists(Path.Combine(generated.ProjectDirectory, "AssetTypeSummaryHandler.cs")).Should().BeTrue();
            File.ReadAllText(Path.Combine(generated.ProjectDirectory, "README.txt"))
                .Should().Contain("layerId (string, optional)");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Generate_MissingHandlerBody_ThrowsInvalidOperationException()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var commandsProjectPath = Path.Combine(tempRoot, "core", "GeoAssets.Commands", "GeoAssets.Commands.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(commandsProjectPath)!);
            File.WriteAllText(commandsProjectPath, "<Project />");

            var scaffolder = new GeoCommandPluginScaffolder(commandsProjectPath);

            var act = () => scaffolder.Generate(CreateSpec() with { HandlerBody = string.Empty }, Path.Combine(tempRoot, "generated"));

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*handler body*");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static GeoCommandPluginSpec CreateSpec() => new()
    {
        PluginName = "Asset Type Summary",
        CommandName = "asset-type-summary",
        Category = "Reporting",
        Description = "Summarizes assets by asset type.",
        Usings = ["System.Linq"],
        Parameters =
        [
            new GeoCommandPluginParameterSpec
            {
                Name = "layerId",
                Type = "string",
                Description = "Optional layer filter.",
                Required = false
            }
        ],
        HandlerBody =
            """
            var features = context.Repository.GetAll().AsEnumerable();

            if (parameters.TryGetValue("layerId", out var layerValue) && layerValue is string layerId && !string.IsNullOrWhiteSpace(layerId))
                features = features.Where(feature => string.Equals(feature.Properties.LayerId, layerId, StringComparison.OrdinalIgnoreCase));

            var summaries = features
                .GroupBy(feature => feature.Properties.AssetTypeId ?? "unknown")
                .Select(group => new { assetTypeId = group.Key, count = group.Count() })
                .OrderBy(item => item.assetTypeId)
                .ToList();

            return GeoCommandResult.Ok(new
            {
                totalFeatures = summaries.Sum(item => item.count),
                summaries
            });
            """
    };

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"geoassets-commands-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
