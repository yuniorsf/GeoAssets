using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace GeoAssets.Shared.Tests.Services.OrderDetail;

/// <summary>A panel that declares exactly the parameters <c>IOrderDetailPanelRegistry</c> documents, and renders a marker.</summary>
internal class FakeOrderPanel : ComponentBase
{
    [Parameter] public IServiceOrder? Order { get; set; }
    [Parameter] public WorkflowPrincipal? Principal { get; set; }
    [Parameter] public EventCallback OnChanged { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "fake-order-panel");
        builder.AddContent(2, Order?.Id);
        builder.CloseElement();
    }
}
