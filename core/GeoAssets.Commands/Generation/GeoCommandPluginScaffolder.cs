using System.Text;
using System.Text.RegularExpressions;

namespace GeoAssets.Commands.Generation;

public sealed class GeoCommandPluginScaffolder(string commandsProjectPath)
{
    private static readonly Regex NameSegmentPattern = new("[A-Za-z0-9]+", RegexOptions.Compiled);

    public string CommandsProjectPath { get; } =
        string.IsNullOrWhiteSpace(commandsProjectPath)
            ? throw new ArgumentException("Commands project path is required.", nameof(commandsProjectPath))
            : Path.GetFullPath(commandsProjectPath);

    public GeneratedCommandPlugin Generate(GeoCommandPluginSpec spec, string outputRootDirectory)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRootDirectory);

        var pluginSuffix = NormalizePluginName(spec.PluginName);
        var assemblyName = $"GeoAssets.Plugin.{pluginSuffix}";
        var namespaceName = assemblyName;
        var className = $"{pluginSuffix}Handler";
        var projectDirectory = Path.Combine(Path.GetFullPath(outputRootDirectory), assemblyName);
        var commandsProjectReferencePath = NormalizePath(Path.GetRelativePath(projectDirectory, CommandsProjectPath));

        if (string.IsNullOrWhiteSpace(spec.CommandName))
            throw new InvalidOperationException("Plugin spec must include a command name.");

        if (string.IsNullOrWhiteSpace(spec.Description))
            throw new InvalidOperationException("Plugin spec must include a description.");

        if (string.IsNullOrWhiteSpace(spec.HandlerBody))
            throw new InvalidOperationException("Plugin spec must include handler body statements.");

        var files = new List<GeneratedPluginFile>
        {
            new($"{assemblyName}.csproj", BuildProjectFile(assemblyName, namespaceName, commandsProjectReferencePath)),
            new($"{className}.cs", BuildHandlerFile(spec, namespaceName, className)),
            new("README.txt", BuildReadme(spec, assemblyName, className)),
        };

        return new GeneratedCommandPlugin(assemblyName, namespaceName, projectDirectory, files);
    }

    public async Task<GeneratedCommandPlugin> WriteToDirectoryAsync(
        GeoCommandPluginSpec spec,
        string outputRootDirectory,
        CancellationToken ct = default)
    {
        var generated = Generate(spec, outputRootDirectory);
        Directory.CreateDirectory(generated.ProjectDirectory);

        foreach (var file in generated.Files)
        {
            var targetPath = Path.Combine(generated.ProjectDirectory, file.RelativePath);
            var parent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            await File.WriteAllTextAsync(targetPath, file.Content, ct);
        }

        return generated;
    }

    private static string BuildProjectFile(string assemblyName, string namespaceName, string commandsProjectReferencePath) =>
        $$"""
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <RootNamespace>{{namespaceName}}</RootNamespace>
            <AssemblyName>{{assemblyName}}</AssemblyName>
          </PropertyGroup>

          <ItemGroup>
            <ProjectReference Include="{{commandsProjectReferencePath}}" />
          </ItemGroup>

        </Project>
        """;

    private static string BuildHandlerFile(GeoCommandPluginSpec spec, string namespaceName, string className)
    {
        var usings = spec.Usings
            .Append("GeoAssets.Commands.Contracts")
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(u => u, StringComparer.Ordinal)
            .Select(u => $"using {u};");

        var builder = new StringBuilder();
        foreach (var @using in usings)
            builder.AppendLine(@using);

        builder.AppendLine();
        builder.AppendLine($"namespace {namespaceName};");
        builder.AppendLine();
        builder.AppendLine($"[ExportGeoCommand(\"{EscapeString(spec.CommandName)}\",");
        builder.AppendLine($"    Category    = \"{EscapeString(spec.Category)}\",");
        builder.AppendLine($"    Description = \"{EscapeString(spec.Description)}\")]");
        builder.AppendLine($"public sealed class {className} : IGeoCommandHandler");
        builder.AppendLine("{");
        builder.AppendLine("    public async Task<GeoCommandResult> ExecuteAsync(");
        builder.AppendLine("        GeoCommandContext context,");
        builder.AppendLine("        IReadOnlyDictionary<string, object> parameters,");
        builder.AppendLine("        CancellationToken ct = default)");
        builder.AppendLine("    {");
        builder.AppendLine(IndentBlock(spec.HandlerBody, 8));
        builder.AppendLine("    }");
        builder.AppendLine("}");

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string BuildReadme(GeoCommandPluginSpec spec, string assemblyName, string className)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Assembly: {assemblyName}");
        builder.AppendLine($"Handler: {className}");
        builder.AppendLine($"Command: {spec.CommandName}");
        builder.AppendLine($"Category: {spec.Category}");
        builder.AppendLine($"Description: {spec.Description}");
        builder.AppendLine();
        builder.AppendLine("Parameters:");

        if (spec.Parameters.Count == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (var parameter in spec.Parameters)
            {
                var requiredLabel = parameter.Required ? "required" : "optional";
                builder.AppendLine($"- {parameter.Name} ({parameter.Type}, {requiredLabel}): {parameter.Description}");
            }
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string NormalizePluginName(string pluginName)
    {
        if (string.IsNullOrWhiteSpace(pluginName))
            throw new InvalidOperationException("Plugin spec must include a plugin name.");

        var segments = NameSegmentPattern.Matches(pluginName)
            .Select(m => m.Value)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (segments.Count == 0)
            throw new InvalidOperationException("Plugin name must contain at least one alphanumeric segment.");

        var normalized = string.Concat(segments.Select(ToPascalCase));
        if (char.IsDigit(normalized[0]))
            normalized = $"Plugin{normalized}";

        return normalized;
    }

    private static string ToPascalCase(string segment) =>
        segment.Length switch
        {
            0 => string.Empty,
            1 => char.ToUpperInvariant(segment[0]).ToString(),
            _ => char.ToUpperInvariant(segment[0]) + segment[1..],
        };

    private static string EscapeString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string IndentBlock(string value, int spaces)
    {
        var pad = new string(' ', spaces);
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal);
        return string.Join(Environment.NewLine, normalized.Split('\n').Select(line => pad + line));
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
