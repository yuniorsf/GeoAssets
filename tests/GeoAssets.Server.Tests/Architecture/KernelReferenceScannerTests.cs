using System.Text.RegularExpressions;
using FluentAssertions;
using GeoAssets.NeutralOperator.Domain;
using GeoAssets.Core.Models;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Provider.Rest;
using GeoAssets.Server.Tests.Architecture.Fixtures;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Server.Tests.Architecture;

[Trait("Category", "Architecture")]
public sealed class KernelReferenceScannerTests
{
    private const string ProviderTypeName = "GeoAssets.Provider.Rest.GeoCoreFederationOptions";

    public static TheoryData<Type, bool> NamespaceCases => new()
    {
        { typeof(GeoCoreFederationOptions), true },
        { typeof(GeoFeature), false },
        { typeof(GeoAssetsMeter), false },
        { typeof(OrderTypeRegistry), false },
        { typeof(OccupancyCommitResult), true },
        { typeof(IspAssetTypes), true },
    };

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Scan()
    {
        var types = typeof(CleanFixture).Assembly.GetTypes()
            .Where(t => string.Equals(t.Namespace, typeof(CleanFixture).Namespace, StringComparison.Ordinal));
        return KernelReferenceScanner.FindForbiddenReferences(types, KernelReferenceRatchet.IsForbidden);
    }

    private static void AssertReported(Type owner)
    {
        var result = Scan();
        result.Should().ContainKey(owner.FullName!);
        result[owner.FullName!].Should().Contain(ProviderTypeName);
    }

    [Fact]
    public void FindForbiddenReferences_CleanType_ReportsNothing() =>
        Scan().Should().NotContainKey(typeof(CleanFixture).FullName!);

    [Fact]
    public void FindForbiddenReferences_FieldOfProviderType_IsReported() =>
        AssertReported(typeof(FieldReferenceFixture));

    [Fact]
    public void FindForbiddenReferences_ProviderTypeUsedOnlyInMethodBody_IsReported() =>
        AssertReported(typeof(MethodBodyOnlyFixture));

    [Fact]
    public void FindForbiddenReferences_ProviderTypeUsedInsideLambda_IsAttributedToDeclaringType()
    {
        AssertReported(typeof(LambdaFixture));
        Scan().Keys.Should().OnlyContain(k => !k.Contains('+', StringComparison.Ordinal));
    }

    [Fact]
    public void FindForbiddenReferences_ProviderTypeUsedInsideAsyncMethod_IsAttributedToDeclaringType() =>
        AssertReported(typeof(AsyncFixture));

    [Fact]
    public void FindForbiddenReferences_ProviderTypeAsGenericArgument_IsReported() =>
        AssertReported(typeof(GenericArgumentFixture));

    [Fact]
    public void FindForbiddenReferences_NestedType_IsAttributedToOutermostType()
    {
        var result = Scan();

        result.Should().ContainKey(typeof(OuterFixture).FullName!);
        result.Keys.Should().NotContain(k => k.Contains("OuterFixture+", StringComparison.Ordinal));
    }

    [Fact]
    public void OwnerOf_NestedType_ReturnsOutermostFullName()
    {
        KernelReferenceScanner.OwnerOf(typeof(OuterFixture.Inner)).Should().Be(typeof(OuterFixture).FullName);
        KernelReferenceScanner.OwnerOf(typeof(OuterFixture)).Should().Be(typeof(OuterFixture).FullName);
    }

    [Theory]
    [MemberData(nameof(NamespaceCases))]
    public void IsForbidden_MatchesWholeNamespaceSegments(Type type, bool expected) =>
        KernelReferenceRatchet.IsForbidden(type).Should().Be(expected);

    [Fact]
    public void Evaluate_ViolationNotInAllowlist_IsNewOffender()
    {
        var violations = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal) { ["A"] = new HashSet<string>(StringComparer.Ordinal) { "X" } };
        var allowlist = new Dictionary<string, string>(StringComparer.Ordinal);

        var result = KernelReferenceRatchet.Evaluate(violations, allowlist);

        result.NewOffenders.Should().Equal("A");
        result.StaleAllowlistEntries.Should().BeEmpty();
        result.IsClean.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_AllowlistedOwnerWithoutViolation_IsStale()
    {
        var violations = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        var allowlist = new Dictionary<string, string>(StringComparer.Ordinal) { ["B"] = "XD01-345 (P1)" };

        var result = KernelReferenceRatchet.Evaluate(violations, allowlist);

        result.NewOffenders.Should().BeEmpty();
        result.StaleAllowlistEntries.Should().Equal("B");
        result.IsClean.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_ViolationsMatchAllowlist_IsClean()
    {
        var violations = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal) { ["A"] = new HashSet<string>(StringComparer.Ordinal) { "X" } };
        var allowlist = new Dictionary<string, string>(StringComparer.Ordinal) { ["A"] = "XD01-345 (P1)" };

        KernelReferenceRatchet.Evaluate(violations, allowlist).IsClean.Should().BeTrue();
    }

    [Fact]
    public void Allowlist_EveryEntryNamesTheEpicThatRemovesIt()
    {
        var pattern = new Regex(@"^XD01-3(4[4-9]|5[01]) \(P[0-7]\)", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

        // XD01-385 removed the last entry: the ratchet is at zero. A future entry must still name the epic that removes it.
        KernelReferenceRatchet.Allowlist.Should().BeEmpty("the kernel reference ratchet reached zero with XD01-385 — never add an entry back");
        KernelReferenceRatchet.Allowlist.Values.Where(v => !pattern.IsMatch(v)).Should().BeEmpty("every entry names the epic that removes it");
    }
}
