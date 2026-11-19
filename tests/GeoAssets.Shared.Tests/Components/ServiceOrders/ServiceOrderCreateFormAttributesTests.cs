using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Localization;
using GeoAssets.Shared.Components.Assets;
using GeoAssets.Shared.Components.ServiceOrders;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.ServiceOrders;

/// <summary>
/// bUnit coverage of the create form's schema-driven attributes (XD01-378): the chosen type's
/// <see cref="OrderType.AttributesSchemaJson"/> is rendered as fields, validated before the order is created, and sent
/// as <see cref="ServiceOrder.Attributes"/>; a type with no schema creates as it always did.
/// </summary>
public sealed class ServiceOrderCreateFormAttributesTests : BunitContext
{
    private const string SchemaTypeId = "with-schema";
    private const string PlainTypeId = "plain";
    private const string TicketLabel = "Ticket number";

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "ticketNumber": { "type": "string", "title": "Ticket number" },
            "crew": { "type": "string", "enum": ["north", "south"], "title": "Crew" }
          },
          "required": ["ticketNumber"]
        }
        """;

    private static readonly WorkflowPrincipal Principal = new("user-1", null, [], [], []);

    private readonly RecordingOrderRepository _repository = new();
    private readonly List<IServiceOrder> _saved = [];

    private IRenderedComponent<ServiceOrderCreateForm> RenderForm(string? initialTypeId = null)
    {
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType { Id = PlainTypeId, DisplayName = "Plain" });
        registry.Register(new OrderType { Id = SchemaTypeId, DisplayName = "With schema", AttributesSchemaJson = Schema });

        var selection = new MapSelectionSet();
        Services.AddSingleton(registry);
        Services.AddSingleton(new ServiceOrderRules(registry));
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<IMapSelectionSet>(selection);
        Services.AddSingleton(new MapSelectionFenceMemory(selection));
        Services.AddSingleton<IAssetProvider>(new TestAssetProvider());
        Services.AddSingleton<IServiceOrderRepository>(_repository);
        Services.AddSingleton<IMapOrderFocus>(new UnusedMapOrderFocus());
        Services.AddSingleton<IJsonStringLocalizer>(new KeyLocalizer());
        Services.AddSingleton(NullLogger<ServiceOrderCreateForm>.Instance);

        return Render<ServiceOrderCreateForm>(p => p
            .Add(c => c.Principal, Principal)
            .Add(c => c.InitialOrderTypeId, initialTypeId)
            .Add(c => c.OnSave, EventCallback.Factory.Create<IServiceOrder>(this, _saved.Add)));
    }

    private static IElement Field(IRenderedComponent<ServiceOrderCreateForm> cut, string label) =>
        cut.FindAll(".form-group").Single(g => g.QuerySelector("label")?.TextContent.Contains(label, StringComparison.Ordinal) == true);

    private static Task FillTitleAsync(IRenderedComponent<ServiceOrderCreateForm> cut, string title) =>
        Field(cut, "orders.orderTitle").QuerySelector("input")!.ChangeAsync(new ChangeEventArgs { Value = title });

    private static Task FillTicketAsync(IRenderedComponent<ServiceOrderCreateForm> cut, string value) =>
        Field(cut, TicketLabel).QuerySelector("input")!.ChangeAsync(new ChangeEventArgs { Value = value });

    private static string AttributeErrors(IRenderedComponent<ServiceOrderCreateForm> cut) =>
        string.Join(' ', cut.FindAll(".attribute-errors").Select(e => e.TextContent));

    private static Task SubmitAsync(IRenderedComponent<ServiceOrderCreateForm> cut) => cut.Find("form").SubmitAsync();

    [Fact]
    public void Form_ATypeWithASchema_RendersItsFields()
    {
        var cut = RenderForm(SchemaTypeId);

        cut.FindComponents<SchemaDrivenAttributeEditor>().Should().ContainSingle();
        Field(cut, TicketLabel).TextContent.Should().Contain("*", "ticketNumber is required");
        Field(cut, "Crew").QuerySelectorAll("option").Select(o => o.TextContent).Should().Contain(["north", "south"]);
    }

    [Fact]
    public async Task Form_ChoosingATypeWithASchema_ShowsItsFields_AndChoosingOneWithoutHidesThem()
    {
        var cut = RenderForm(PlainTypeId);
        cut.FindComponents<SchemaDrivenAttributeEditor>().Should().BeEmpty();

        await cut.Find("#order-type-select").ChangeAsync(new ChangeEventArgs { Value = SchemaTypeId });
        cut.FindComponents<SchemaDrivenAttributeEditor>().Should().ContainSingle();

        await cut.Find("#order-type-select").ChangeAsync(new ChangeEventArgs { Value = PlainTypeId });
        cut.FindComponents<SchemaDrivenAttributeEditor>().Should().BeEmpty();
    }

    [Fact]
    public async Task Form_ARequiredFieldMissing_ShowsTheErrorAndNeverCallsTheOrderService()
    {
        var cut = RenderForm(SchemaTypeId);
        await FillTitleAsync(cut, "Pole work");

        await SubmitAsync(cut);

        _repository.Added.Should().BeEmpty();
        _saved.Should().BeEmpty();
        var expected = ServiceOrderAttributeValidator.Validate(cut.Services.GetRequiredService<OrderTypeRegistry>().Find(SchemaTypeId)!, new Dictionary<string, string>(StringComparer.Ordinal));
        expected.Should().NotBeEmpty();
        AttributeErrors(cut).Should().NotBeNullOrWhiteSpace();
        foreach (var error in expected) AttributeErrors(cut).Should().Contain(error);
    }

    [Fact]
    public async Task Form_ABlankRequiredField_IsTreatedAsMissing()
    {
        var cut = RenderForm(SchemaTypeId);
        await FillTitleAsync(cut, "Pole work");
        await FillTicketAsync(cut, string.Empty);

        await SubmitAsync(cut);

        _repository.Added.Should().BeEmpty();
        AttributeErrors(cut).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Form_ValidAttributes_AreSentWithTheCreatedOrder_AndLeftOutFieldsAreOmitted()
    {
        var cut = RenderForm(SchemaTypeId);
        await FillTitleAsync(cut, "Pole work");
        await FillTicketAsync(cut, "T-42");

        await SubmitAsync(cut);

        var order = _repository.Added.Should().ContainSingle().Subject;
        order.OrderTypeId.Should().Be(SchemaTypeId);
        order.Attributes.Should().Equal(new Dictionary<string, string>(StringComparer.Ordinal) { ["ticketNumber"] = "T-42" });
        _saved.Should().ContainSingle().Which.Should().BeSameAs(order);
        cut.FindAll(".attribute-errors").Should().BeEmpty();
    }

    [Fact]
    public async Task Form_AValueEnteredForAnOptionalField_IsSentToo()
    {
        var cut = RenderForm(SchemaTypeId);
        await FillTitleAsync(cut, "Pole work");
        await FillTicketAsync(cut, "T-42");
        await Field(cut, "Crew").QuerySelector("select")!.ChangeAsync(new ChangeEventArgs { Value = "south" });

        await SubmitAsync(cut);

        _repository.Added.Should().ContainSingle().Which.Attributes
            .Should().Equal(new Dictionary<string, string>(StringComparer.Ordinal) { ["ticketNumber"] = "T-42", ["crew"] = "south" });
    }

    [Fact]
    public async Task Form_AttributeValidationExceptionFromTheOrderService_ShowsItsErrors_AndDoesNotSave()
    {
        _repository.AddFailure = new ServiceOrderAttributeValidationException(SchemaTypeId, ["The server says ticketNumber is not on file."]);
        var cut = RenderForm(SchemaTypeId);
        await FillTitleAsync(cut, "Pole work");
        await FillTicketAsync(cut, "T-42");

        await SubmitAsync(cut);

        _saved.Should().BeEmpty();
        AttributeErrors(cut).Should().Contain("The server says ticketNumber is not on file.");
    }

    [Fact]
    public async Task Form_ATypeWithNoSchema_CreatesAsBefore_WithNoEditorAndNoAttributes()
    {
        var cut = RenderForm(PlainTypeId);
        cut.FindComponents<SchemaDrivenAttributeEditor>().Should().BeEmpty();
        await FillTitleAsync(cut, "Plain work");

        await SubmitAsync(cut);

        var order = _repository.Added.Should().ContainSingle().Subject;
        order.OrderTypeId.Should().Be(PlainTypeId);
        order.Attributes.Should().BeEmpty();
        _saved.Should().ContainSingle();
        cut.FindAll(".attribute-errors").Should().BeEmpty();
    }
}
