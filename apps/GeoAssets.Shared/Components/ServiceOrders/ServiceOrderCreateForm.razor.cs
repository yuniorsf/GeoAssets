using System.ComponentModel.DataAnnotations;
using GeoAssets.Shared.Components.Assets;
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

    /// <summary>The order type the form starts with when it is offered (XD01-370: a map feature action's).</summary>
    [Parameter] public string? InitialOrderTypeId { get; set; }

    private readonly NewOrderModel _model = new();
    private readonly CancellationTokenSource _lifetime = new();
    private List<OrderType> _allowedTypes = [];

    // ─── The chosen type's attributes (XD01-378) ──────────────────────────
    private readonly Dictionary<string, string> _attributes = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _attributeErrors = [];

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

    private OrderType? SelectedType => OrderTypes.Find(_model.OrderTypeId);

    /// <summary>The chosen type's <see cref="OrderType.AttributesSchemaJson"/>, which the form renders as fields.</summary>
    private string SelectedSchema => SelectedType?.AttributesSchemaJson ?? string.Empty;

    private bool HasSchema => !string.IsNullOrWhiteSpace(SelectedSchema);

    /// <summary>
    /// <see cref="_attributeErrors"/> not claimed by a field <see cref="SchemaDrivenAttributeEditor"/>
    /// renders (it shows its own inline), e.g. a "required" violation, whose location is the object
    /// root — as <c>AssetForm</c> lists them.
    /// </summary>
    private IReadOnlyList<string> UnmatchedAttributeErrors =>
        HasSchema
            ? [.. _attributeErrors.Where(e => !SchemaDrivenAttributeEditor.ParseFields(SelectedSchema)
                .Any(f => e.Contains(f.Key, StringComparison.OrdinalIgnoreCase)))]
            : _attributeErrors;

    protected override void OnParametersSet()
    {
        _allowedTypes = [.. OrderTypes.All.Where(t => Rules.CanCreate(Principal, t))];

        if (!_selectionModeInitialized)
        {
            _selectionModeInitialized = true;
            _useSelection = SelectionOrderTypes.UseSelectionInitially(FromSelection, SelectionSet.Ids.Count);
            if (!string.IsNullOrEmpty(InitialOrderTypeId)) _model.OrderTypeId = InitialOrderTypeId;
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
            ? SelectionOrderTypes.Evaluate(_allowedTypes, SelectionSet.Ids, AssetTypeOf, AssetProvider.GetAssetTypes())
            : [];

        IReadOnlyList<OrderType> offered = _useSelection ? [.. _fits.Select(f => f.Type)] : _allowedTypes;
        _model.OrderTypeId = SelectionOrderTypes.KeepOrFirst(_model.OrderTypeId, offered);
    }

    private string? AssetTypeOf(string featureId) => AssetProvider.GetById(featureId)?.Properties.AssetTypeId;

    /// <summary>
    /// The attributes the new order is created with: the values entered for the chosen type's
    /// schema fields, left-out fields omitted — so a required one is reported missing.
    /// </summary>
    private Dictionary<string, string> EnteredAttributes() =>
        SchemaDrivenAttributeEditor.ParseFields(SelectedSchema)
            .Where(f => _attributes.TryGetValue(f.Key, out var value) && !string.IsNullOrEmpty(value))
            .ToDictionary(f => f.Key, f => _attributes[f.Key], StringComparer.Ordinal);

    private async Task HandleSave()
    {
        var attributes = EnteredAttributes();
        _attributeErrors = SelectedType is { } selectedType ? ServiceOrderAttributeValidator.Validate(selectedType, attributes) : [];
        if (_attributeErrors.Count > 0) return;

        OrderTargetSelectionPlan? plan = null;
        if (_useSelection)
        {
            var policy = OrderTypes.Find(_model.OrderTypeId)?.TargetPolicy;
            var planned = policy is null
                ? OrderTargetSelectionPlanResult.Failed(OrderTargetSelectionPlanProblem.NoEligibleFeature)
                : OrderTargetSelectionPlanner.Plan(policy, SelectionSet.Ids, FenceMemory.Rings, AssetTypeOf, AssetProvider.GetAssetTypes());

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
            Attributes     = attributes,
        };

        try
        {
            await Repository.AddAsync(order);
        }
        catch (ServiceOrderAttributeValidationException ex)
        {
            _attributeErrors = ex.Errors;
            return;
        }

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
