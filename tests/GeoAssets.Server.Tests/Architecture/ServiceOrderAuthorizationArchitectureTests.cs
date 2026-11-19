using FluentAssertions;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Server.Tests.Architecture;

/// <summary>
/// The single order-authorization decision point (XD01-364, ADR-001 smell H): a route that loads a service order decides
/// with <c>ServiceOrderRules</c> — through <see cref="ServiceOrderActionRequirement"/>, <see cref="ServiceOrderCreateRequirement"/>
/// or the organization gate — never with <see cref="OrgResourceRequirement"/>, which knows nothing of the order type's
/// policies, the dispatch or the organization gate. Scanned in the IL of the server assembly, as the kernel ratchet is.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class ServiceOrderAuthorizationArchitectureTests
{
    [Fact]
    public void NoEndpointLoadingAServiceOrder_UsesOrgResourceRequirement()
    {
        var orgResource = typeof(OrgResourceRequirement).FullName!;
        var serviceOrderTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            typeof(IServiceOrder).FullName!,
            typeof(IServiceOrderReader).FullName!,
            typeof(IServiceOrderRepository).FullName!,
        };

        var references = KernelReferenceScanner.FindForbiddenReferences(
            typeof(OrgResourceRequirement).Assembly.GetTypes(),
            type => string.Equals(type.FullName, orgResource, StringComparison.Ordinal) || serviceOrderTypes.Contains(type.FullName ?? string.Empty));

        var offenders = references
            .Where(kv => kv.Value.Contains(orgResource, StringComparer.Ordinal) && kv.Value.Overlaps(serviceOrderTypes))
            .Select(kv => kv.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            because: "a route that loads a service order must authorize with ServiceOrderActionRequirement / ServiceOrderCreateRequirement (ServiceOrderRules), not with OrgResourceRequirement");
    }

    [Fact]
    public void TheScan_SeesAHandlerThatReferencesBothSides()
    {
        // The scan is only worth its answer if it can see what it forbids: the handler that answers the action
        // requirement references the service-order interface, and the organization handler the org requirement.
        var orgResource = typeof(OrgResourceRequirement).FullName!;
        var references = KernelReferenceScanner.FindForbiddenReferences(
            [typeof(ServiceOrderActionAuthorizationHandler), typeof(OrgResourceAuthorizationHandler)],
            type => string.Equals(type.FullName, orgResource, StringComparison.Ordinal)
                || string.Equals(type.FullName, typeof(IServiceOrder).FullName, StringComparison.Ordinal));

        references[typeof(ServiceOrderActionAuthorizationHandler).FullName!].Should().Contain(typeof(IServiceOrder).FullName!);
        references[typeof(OrgResourceAuthorizationHandler).FullName!].Should().Contain(orgResource);
    }
}
