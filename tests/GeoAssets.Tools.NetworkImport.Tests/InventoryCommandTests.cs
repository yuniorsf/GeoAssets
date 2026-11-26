using System.Text.Json;
using FluentAssertions;
using GeoAssets.Tools.NetworkImport.Tests.Support;
using Xunit;

namespace GeoAssets.Tools.NetworkImport.Tests;

public sealed class InventoryCommandTests
{
    private static Task<CliResult> RunAsync(CoelbaDelivery delivery, TempDirectory output) =>
        CliRunner.RunAsync("inventory", "--root", delivery.Root, "--profile", "neoenergia-gse", "--out", output.Path);

    [Fact]
    public async Task Inventory_WritesInventoryJsonAndDataRequestAndExitsZero()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();

        var result = await RunAsync(delivery, output);

        result.ExitCode.Should().Be(0);
        File.Exists(output.File("inventory.json")).Should().BeTrue();
        File.Exists(output.File("data-request.md")).Should().BeTrue();
        result.Stdout.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Inventory_JsonListsEveryLayerWithCamelCaseProperties()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();

        await RunAsync(delivery, output);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output.File("inventory.json")));
        var layers = document.RootElement.GetProperty("layers").EnumerateArray().ToArray();
        layers.Select(l => l.GetProperty("layer").GetString()).Should().BeEquivalentTo(["POSTE", "INSTALACAO", "REDE_MT"]);
        layers.Should().OnlyContain(l => l.GetProperty("recordCount").GetInt64() > 0);
        document.RootElement.GetProperty("profileId").GetString().Should().Be("neoenergia-gse");
    }

    [Fact]
    public async Task Inventory_DataRequestListsTheCodeTablesTheProfileCannotDecode()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();

        await RunAsync(delivery, output);

        var markdown = await File.ReadAllTextAsync(output.File("data-request.md"));
        markdown.Should().Contain("CD_TIPO_PG").And.Contain("CD_INDD_MATERIAL_PG").And.Contain("CD_FASE")
            .And.Contain("CD_TENSAO_NOMINAL").And.Contain("CD_TRAJETO");
    }

    [Fact]
    public async Task Inventory_DataRequestNamesLayersMissingTheirAttributeTable()
    {
        using var delivery = new CoelbaDelivery();
        delivery.AddGeometryOnlyLowVoltage();
        using var output = new TempDirectory();

        await RunAsync(delivery, output);

        var markdown = await File.ReadAllTextAsync(output.File("data-request.md"));
        markdown.Should().Contain("REDE_BT");
    }

    [Fact]
    public async Task Inventory_ReportsRegionsFromSubFolders()
    {
        using var delivery = new CoelbaDelivery();
        delivery.AddRegion("Norte", 2);
        using var output = new TempDirectory();

        await RunAsync(delivery, output);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output.File("inventory.json")));
        document.RootElement.GetProperty("layers").EnumerateArray()
            .Select(l => l.GetProperty("region").GetString()).Should().Contain("Norte");
    }

    [Fact]
    public async Task Inventory_CreatesTheOutputDirectoryWhenMissing()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();
        var nested = Path.Combine(output.Path, "deep", "out");

        var result = await CliRunner.RunAsync("inventory", "--root", delivery.Root, "--profile", "neoenergia-gse", "--out", nested);

        result.ExitCode.Should().Be(0);
        File.Exists(Path.Combine(nested, "inventory.json")).Should().BeTrue();
    }
}
