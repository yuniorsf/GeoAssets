using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Tests.Orders;

/// <summary>
/// A test vertical's verbs (XD01-366, AC c): defined in this assembly, so adding one touches nothing in
/// <c>GeoAssets.Workflow</c>, its wire format or its database.
/// </summary>
internal static class SurveyActions
{
    public static OrderAction Survey { get; } = new("Survey");
}
