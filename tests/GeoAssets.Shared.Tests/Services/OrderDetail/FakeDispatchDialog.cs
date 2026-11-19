using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace GeoAssets.Shared.Tests.Services.OrderDetail;

/// <summary>A dispatch dialog that declares exactly the parameters <c>IOrderDetailPanelRegistry</c> documents, and renders a marker.</summary>
internal class FakeDispatchDialog : ComponentBase
{
    [Parameter] public bool Visible { get; set; }
    [Parameter] public IServiceOrder? Order { get; set; }
    [Parameter] public WorkflowPrincipal? Principal { get; set; }
    [Parameter] public EventCallback OnDispatched { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "fake-dispatch-dialog");
        builder.AddAttribute(2, "data-visible", Visible ? "true" : "false");
        builder.CloseElement();
    }
}
