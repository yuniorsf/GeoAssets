using GeoAssets.Workflow.Registry;

namespace GeoAssets.Workflow.Tests.Registry;

internal static class RegistryTestData
{
    public static FeatureActionDescriptor Action(string role = "occupiable-support", string orderTypeId = "pole-work") =>
        new(role, "create", "featureActions.create", orderTypeId, "/api/workflow/service-orders");

    public static OrderTypeDescriptor Descriptor(
        string verticalId = "vertical-a", string orderTypeId = "pole-work", params FeatureActionDescriptor[] actions) => new()
    {
        VerticalId = verticalId,
        OrderTypeId = orderTypeId,
        DisplayNameKey = "orderTypes.poleWork",
        ApiBaseRoute = "/api/workflow/service-orders",
        FeatureActions = actions,
    };
}
