using System.Text.Json;
using FluentAssertions;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Isp.Bss;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Isp.Bss;

public class IxcSoftOrderAdapterTests
{
    private readonly IxcSoftOrderAdapter _adapter = new();

    [Fact]
    public void ParseOrderSpawn_AllFieldsPresent_ReturnsSpecWithAllFields()
    {
        var payload = """
            {
                "id_cliente": "cli-1",
                "nome": "Jane Doe",
                "endereco": "Rua A, 123",
                "id_plano": "plan-100",
                "fone": "+55 11 99999-0000",
                "email": "jane@example.com"
            }
            """;

        var spec = _adapter.ParseOrderSpawn(payload);

        spec.Should().Be(new BssOrderSpawnSpec("cli-1", "Jane Doe", "Rua A, 123", "plan-100", "+55 11 99999-0000", "jane@example.com"));
    }

    [Fact]
    public void ParseOrderSpawn_OnlyRequiredFieldsPresent_ContactPhoneAndEmailAreNull()
    {
        var payload = """
            {
                "id_cliente": "cli-1",
                "nome": "Jane Doe",
                "endereco": "Rua A, 123",
                "id_plano": "plan-100"
            }
            """;

        var spec = _adapter.ParseOrderSpawn(payload);

        spec.ContactPhone.Should().BeNull();
        spec.ContactEmail.Should().BeNull();
    }

    [Fact]
    public void ParseOrderSpawn_MalformedJson_ThrowsBssOrderSpawnParseException()
    {
        var act = () => _adapter.ParseOrderSpawn("{not valid json");

        act.Should().Throw<BssOrderSpawnParseException>();
    }

    [Fact]
    public void ParseOrderSpawn_MissingIdCliente_ThrowsBssOrderSpawnParseException()
    {
        var payload = """{"nome":"Jane Doe","endereco":"Rua A, 123","id_plano":"plan-100"}""";

        var act = () => _adapter.ParseOrderSpawn(payload);

        act.Should().Throw<BssOrderSpawnParseException>();
    }

    [Fact]
    public void ParseOrderSpawn_BlankIdCliente_ThrowsBssOrderSpawnParseException()
    {
        var payload = """{"id_cliente":"   ","nome":"Jane Doe","endereco":"Rua A, 123","id_plano":"plan-100"}""";

        var act = () => _adapter.ParseOrderSpawn(payload);

        act.Should().Throw<BssOrderSpawnParseException>();
    }

    [Fact]
    public void ParseOrderSpawn_MissingNome_ThrowsBssOrderSpawnParseException()
    {
        var payload = """{"id_cliente":"cli-1","endereco":"Rua A, 123","id_plano":"plan-100"}""";

        var act = () => _adapter.ParseOrderSpawn(payload);

        act.Should().Throw<BssOrderSpawnParseException>();
    }

    [Fact]
    public void ParseOrderSpawn_BlankNome_ThrowsBssOrderSpawnParseException()
    {
        var payload = """{"id_cliente":"cli-1","nome":"  ","endereco":"Rua A, 123","id_plano":"plan-100"}""";

        var act = () => _adapter.ParseOrderSpawn(payload);

        act.Should().Throw<BssOrderSpawnParseException>();
    }

    [Fact]
    public void ParseOrderSpawn_MissingEndereco_ThrowsBssOrderSpawnParseException()
    {
        var payload = """{"id_cliente":"cli-1","nome":"Jane Doe","id_plano":"plan-100"}""";

        var act = () => _adapter.ParseOrderSpawn(payload);

        act.Should().Throw<BssOrderSpawnParseException>();
    }

    [Fact]
    public void ParseOrderSpawn_BlankEndereco_ThrowsBssOrderSpawnParseException()
    {
        var payload = """{"id_cliente":"cli-1","nome":"Jane Doe","endereco":"   ","id_plano":"plan-100"}""";

        var act = () => _adapter.ParseOrderSpawn(payload);

        act.Should().Throw<BssOrderSpawnParseException>();
    }

    [Fact]
    public void ParseOrderSpawn_MissingIdPlano_ThrowsBssOrderSpawnParseException()
    {
        var payload = """{"id_cliente":"cli-1","nome":"Jane Doe","endereco":"Rua A, 123"}""";

        var act = () => _adapter.ParseOrderSpawn(payload);

        act.Should().Throw<BssOrderSpawnParseException>();
    }

    [Fact]
    public void ParseOrderSpawn_BlankIdPlano_ThrowsBssOrderSpawnParseException()
    {
        var payload = """{"id_cliente":"cli-1","nome":"Jane Doe","endereco":"Rua A, 123","id_plano":"  "}""";

        var act = () => _adapter.ParseOrderSpawn(payload);

        act.Should().Throw<BssOrderSpawnParseException>();
    }

    [Fact]
    public void BuildActivationPayload_AllAttributesPresent_WritesAllFields()
    {
        var order = new ServiceOrder
        {
            Attributes =
            {
                [BssOrderAttributes.SubscriberIdKey] = "cli-1",
                [FtthDropInstallChecklistAttributes.OntFsanKey] = "FSAN123",
                [FtthDropInstallChecklistAttributes.OntMacAddressKey] = "AA:BB:CC:DD:EE:FF",
                [FtthDropInstallChecklistAttributes.CtoFeatureIdKey] = "cto-1",
            },
        };

        var payloadJson = _adapter.BuildActivationPayload(order);

        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        root.GetProperty("id_contrato").GetString().Should().Be("cli-1");
        root.GetProperty("status").GetString().Should().Be("ativo");
        root.GetProperty("onu_sn").GetString().Should().Be("FSAN123");
        root.GetProperty("onu_mac").GetString().Should().Be("AA:BB:CC:DD:EE:FF");
        root.GetProperty("cto_id").GetString().Should().Be("cto-1");
    }

    [Fact]
    public void BuildActivationPayload_NoCtoFeatureId_CtoIdPropertyIsAbsent()
    {
        var order = new ServiceOrder
        {
            Attributes =
            {
                [BssOrderAttributes.SubscriberIdKey] = "cli-1",
                [FtthDropInstallChecklistAttributes.OntFsanKey] = "FSAN123",
                [FtthDropInstallChecklistAttributes.OntMacAddressKey] = "AA:BB:CC:DD:EE:FF",
            },
        };

        var payloadJson = _adapter.BuildActivationPayload(order);

        using var document = JsonDocument.Parse(payloadJson);
        document.RootElement.TryGetProperty("cto_id", out _).Should().BeFalse();
    }

    [Fact]
    public void BuildActivationPayload_MissingSubscriberId_ThrowsInvalidOperationException()
    {
        var order = new ServiceOrder
        {
            Attributes =
            {
                [FtthDropInstallChecklistAttributes.OntFsanKey] = "FSAN123",
                [FtthDropInstallChecklistAttributes.OntMacAddressKey] = "AA:BB:CC:DD:EE:FF",
            },
        };

        var act = () => _adapter.BuildActivationPayload(order);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BuildActivationPayload_MissingOntFsan_ThrowsInvalidOperationException()
    {
        var order = new ServiceOrder
        {
            Attributes =
            {
                [BssOrderAttributes.SubscriberIdKey] = "cli-1",
                [FtthDropInstallChecklistAttributes.OntMacAddressKey] = "AA:BB:CC:DD:EE:FF",
            },
        };

        var act = () => _adapter.BuildActivationPayload(order);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BuildActivationPayload_MissingOntMacAddress_ThrowsInvalidOperationException()
    {
        var order = new ServiceOrder
        {
            Attributes =
            {
                [BssOrderAttributes.SubscriberIdKey] = "cli-1",
                [FtthDropInstallChecklistAttributes.OntFsanKey] = "FSAN123",
            },
        };

        var act = () => _adapter.BuildActivationPayload(order);

        act.Should().Throw<InvalidOperationException>();
    }
}
