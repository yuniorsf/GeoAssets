using FluentAssertions;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Workflow.Tests.Targets;

/// <summary>The global target ledger's vocabulary (XD01-379): vertical id rules and the target id derivation.</summary>
public sealed class TargetLedgerTests
{
    [Theory]
    [InlineData("kernel")]
    [InlineData("field-service")]
    [InlineData("a")]
    [InlineData("a1-b2-c3")]
    [InlineData("0")]
    public void IsWellFormedVerticalId_LowerCaseWordsJoinedByHyphens_IsTrue(string verticalId)
    {
        TargetLedger.IsWellFormedVerticalId(verticalId).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-a")]
    [InlineData("a-")]
    [InlineData("a--b")]
    [InlineData("Upper")]
    [InlineData("under_score")]
    [InlineData("white space")]
    [InlineData("a/b")]
    [InlineData("a.b")]
    public void IsWellFormedVerticalId_Malformed_IsFalse(string? verticalId)
    {
        TargetLedger.IsWellFormedVerticalId(verticalId).Should().BeFalse();
    }

    [Fact]
    public void IsWellFormedVerticalId_AtTheLengthLimit_IsTrue_AndOneOverIsFalse()
    {
        TargetLedger.IsWellFormedVerticalId(new string('a', TargetLedger.MaxVerticalIdLength)).Should().BeTrue();
        TargetLedger.IsWellFormedVerticalId(new string('a', TargetLedger.MaxVerticalIdLength + 1)).Should().BeFalse();
    }

    [Fact]
    public void Constants_AreTheAgreedOnes()
    {
        TargetLedger.KernelVerticalId.Should().Be("kernel");
        TargetLedger.MaxVerticalIdLength.Should().Be(64);
        TargetLedger.MaxOrderIdLength.Should().Be(36);
        TargetLedger.ConflictProblemCode.Should().Be("targets.conflict");
    }

    [Fact]
    public void IsRemoteVerticalId_Kernel_IsFalse_AndAnyOtherWellFormedIdIsTrue()
    {
        TargetLedger.IsRemoteVerticalId("kernel").Should().BeFalse();
        TargetLedger.IsRemoteVerticalId("isp").Should().BeTrue();
        TargetLedger.IsRemoteVerticalId("Not Well Formed").Should().BeFalse();
        TargetLedger.IsRemoteVerticalId(null).Should().BeFalse();
        TargetLedger.IsRemoteVerticalId("kernel-two").Should().BeTrue("only the exact id kernel is reserved");
    }

    [Theory]
    [InlineData("kernel", true)]
    [InlineData("Kernel", false)]
    [InlineData("kernel ", false)]
    [InlineData("isp", false)]
    [InlineData(null, false)]
    public void IsKernel_ComparesOrdinally(string? verticalId, bool expected)
    {
        TargetLedger.IsKernel(verticalId).Should().Be(expected);
    }

    [Fact]
    public void TargetId_Kernel_EqualsTheLegacyInProcessId()
    {
        TargetLedger.TargetId("kernel", "ord-1", "feat-1").Should().Be(OrderTargetSelectionService.TargetId("ord-1", "feat-1"));
    }

    [Fact]
    public void TargetId_Remote_IsStable_AndDiffersAcrossVerticalsOrdersAndFeatures()
    {
        var id = TargetLedger.TargetId("isp", "ord-1", "feat-1");

        TargetLedger.TargetId("isp", "ord-1", "feat-1").Should().Be(id, "the id is derived, not generated");
        Guid.TryParse(id, out _).Should().BeTrue();
        id.Length.Should().BeLessThanOrEqualTo(36);
        new[]
        {
            id,
            TargetLedger.TargetId("other", "ord-1", "feat-1"),
            TargetLedger.TargetId("isp", "ord-2", "feat-1"),
            TargetLedger.TargetId("isp", "ord-1", "feat-2"),
            TargetLedger.TargetId("kernel", "ord-1", "feat-1"),
        }.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(null, "o", "f")]
    [InlineData(" ", "o", "f")]
    [InlineData("v", "", "f")]
    [InlineData("v", "o", null)]
    public void TargetId_BlankPart_Throws(string? verticalId, string? orderId, string? featureId)
    {
        var act = () => TargetLedger.TargetId(verticalId!, orderId!, featureId!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ServiceOrderTarget_VerticalId_DefaultsToKernel_AndRejectsBlank()
    {
        var target = new ServiceOrderTarget
        {
            Id = "t", OrderId = "o", FeatureId = "f", AssetTypeId = "a", Status = OrderTargetStatus.Pending,
            AddedAt = DateTime.UnixEpoch, AddedBy = "u", OrganizationId = Guid.NewGuid(),
        };

        target.VerticalId.Should().Be(TargetLedger.KernelVerticalId);
        var act = () => target with { VerticalId = " " };
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TargetLedgerOrder_Key_IsVerticalAndOrder_AndBlankPartsThrow()
    {
        var policy = new OrderTargetPolicy { TargetAssetTypeIds = ["pole"] };

        new TargetLedgerOrder("isp", "o1", "t1", Guid.NewGuid(), policy).Key.Should().Be(new TargetLedgerKey("isp", "o1"));
        var act = () => new TargetLedgerOrder("isp", " ", "t1", Guid.NewGuid(), policy);
        act.Should().Throw<ArgumentException>();
        var nullPolicy = () => new TargetLedgerOrder("isp", "o1", "t1", Guid.NewGuid(), null!);
        nullPolicy.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TargetLedgerConflictException_ExposesItsConflicts_AndRejectsNull()
    {
        var conflict = new TargetLedgerConflict("f1", "isp", OrderTargetExclusivity.BlockOnOverlap, null, null);

        new TargetLedgerConflictException([conflict]).Conflicts.Should().Equal(conflict);
        new TargetLedgerConflictException().Conflicts.Should().BeEmpty();
        new TargetLedgerConflictException("m").Conflicts.Should().BeEmpty();
        new TargetLedgerConflictException("m", new InvalidOperationException()).Conflicts.Should().BeEmpty();
        var act = () => new TargetLedgerConflictException((IReadOnlyList<TargetLedgerConflict>)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TargetId_OrderOrFeatureIdsContainingTheSeparator_DoNotCollide()
    {
        var left = TargetLedger.TargetId("isp", "a|b", "c");
        var right = TargetLedger.TargetId("isp", "a", "b|c");

        left.Should().NotBe(right);
        TargetLedger.TargetId("isp", "a|b", "c").Should().Be(left);
    }
}
