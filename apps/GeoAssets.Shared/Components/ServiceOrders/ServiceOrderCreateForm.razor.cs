using System.ComponentModel.DataAnnotations;
using GeoAssets.Shared.Services.OrderTargets;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.ServiceOrders;

public partial class ServiceOrderCreateForm
{
    [Parameter, EditorRequired] public WorkflowPrincipal Principal { get; set; } = null!;
    [Parameter] public EventCallback<IServiceOrder> OnSave { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    /// <summary>
    /// Opens the form with the map selection as the new order's targets (XD01-312), as the map's
    /// "Create order" button asks; the user can still switch it off.
    /// </summary>
    [Parameter] public bool FromSelection { get; set; }

    private readonly NewOrderModel _model = new();
    private readonly CancellationTokenSource _lifetime = new();
    private List<OrderType> _allowedTypes = [];

    // ─── Create from the map selection (XD01-312) ─────────────────────────
    private bool _selectionModeInitialized;
    private bool _useSelection;
    private IReadOnlyList<SelectionOrderTypeFit> _fits = [];
    private string? _planProblemKey;
    private IServiceOrder? _createdOrder;
    private OrderTargetSelectionPlan? _plan;
    private int _plannedSelectionCount;

    /// <summary>The fit of the chosen order type, while the selection is used.</summary>
    private SelectionOrderTypeFit? SelectedFit =>
        _useSelection ? _fits.FirstOrDefault(f => string.Equals(f.Type.Id, _model.OrderTypeId, StringComparison.Ordinal)) : null;

    /// <summary><see cref="SelectedFit"/> when it has something to warn about.</summary>
    private SelectionOrderTypeFit? HintFit => SelectedFit is { } fit && (fit.OtherTypeCount > 0 || fit.MayExceedLimit) ? fit : null;

    protected override void OnParametersSet()
    {
        _allowedTypes = [.. OrderTypes.All.Where(t => Rules.CanCreate(Principal, t))];

        if (!_selectionModeInitialized)
        {
            _selectionModeInitialized = true;
            _useSelection = SelectionOrderTypes.UseSelectionInitially(FromSelection, SelectionSet.Ids.Count);
        }

        RefreshOrderTypes();
    }

    private void OnUseSelectionChanged(ChangeEventArgs e)
    {
        _useSelection = e.Value is true;
        _planProblemKey = null;
        RefreshOrderTypes();
    }

    /// <summary>Recomputes the offered types and keeps the chosen one when it is still offered.</summary>
    private void RefreshOrderTypes()
    {
        _fits = _useSelection
            ? SelectionOrderTypes.Evaluate(_allowedTypes, SelectionSet.Ids, AssetTypeOf)
            : [];

        IReadOnlyList<OrderType> offered = _useSelection ? [.. _fits.Select(f => f.Type)] : _allowedTypes;
        _model.OrderTypeId = SelectionOrderTypes.KeepOrFirst(_model.OrderTypeId, offered);
    }

    private string? AssetTypeOf(string featureId) => AssetProvider.GetById(featureId)?.Properties.AssetTypeId;

    private async Task HandleSave()
    {
        OrderTargetSelectionPlan? plan = null;
        if (_useSelection)
        {
            var policy = OrderTypes.Find(_model.OrderTypeId)?.TargetPolicy;
            var planned = policy is null
                ? OrderTargetSelectionPlanResult.Failed(OrderTargetSelectionPlanProblem.NoEligibleFeature)
                : OrderTargetSelectionPlanner.Plan(policy, SelectionSet.Ids, FenceMemory.Rings, AssetTypeOf);

            if (planned.Problem is { } problem)
            {
                _planProblemKey = OrderTargetMessages.PlanProblemKey(problem);
                return;
            }

            plan = planned.Plan;
        }

        _planProblemKey = null;
        var order = new ServiceOrder
        {
            Title       = _model.Title,
            Description = _model.Description,
            OrderTypeId = _model.OrderTypeId,
            Priority    = _model.Priority,
            CreatedBy   = Principal.UserId,
            CreatedAt   = Clock.GetUtcNow().UtcDateTime,
            // XD01-332: an order belongs to its creator's organization. The server stamps the
            // same value on a body that omits it and refuses any other.
            OrganizationId = Guid.TryParse(Principal.OrganizationId, out var organizationId) ? organizationId : Guid.Empty,
        };

        await Repository.AddAsync(order);

        if (plan is null)
        {
            await OnSave.InvokeAsync(order);
            return;
        }

        // XD01-312: targets can only be previewed against an existing order, so the order is
        // created first and the preview panel takes over.
        (_createdOrder, _plan, _plannedSelectionCount) = (order, plan, SelectionSet.Ids.Count);
    }

    /// <summary>The selection became the order's targets: it is used up, and the map shows the order instead.</summary>
    private async Task OnTargetsApplied(TargetSelectionResult _)
    {
        var order = _createdOrder!;
        SelectionSet.Clear();
        await CreatedOrderMapFocus.FocusAsync(MapFocus, order.Id, Logger, _lifetime.Token);
        await OnSave.InvokeAsync(order);
    }

    private Task OnTargetsSkipped() => OnSave.InvokeAsync(_createdOrder!);

    private Task Cancel() => OnCancel.InvokeAsync();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class NewOrderModel
    {
        [Required]
        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        [Required]
        public string OrderTypeId { get; set; } = string.Empty;

        public ServiceOrderPriority Priority { get; set; } = ServiceOrderPriority.Normal;
    }
}
