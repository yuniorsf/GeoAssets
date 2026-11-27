using FluentAssertions;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

public sealed class CatalogEnumerationLimitExceptionTests
{
    [Fact]
    public void TheOperationAndLimitConstructor_SetsBoth_AndTheMessageNamesBoth()
    {
        var exception = new CatalogEnumerationLimitException("GetDescendants", 10_000);

        exception.Operation.Should().Be("GetDescendants");
        exception.Limit.Should().Be(10_000);
        exception.Message.Should().Contain("GetDescendants").And.Contain("10000");
        exception.Should().BeAssignableTo<InvalidOperationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankOperation_IsRefused(string? operation)
    {
        var create = () => new CatalogEnumerationLimitException(operation!, 5);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TheStandardConstructors_LeaveTheOperationEmptyAndTheLimitZero()
    {
        var inner = new InvalidOperationException("inner");

        var plain = new CatalogEnumerationLimitException();
        var withMessage = new CatalogEnumerationLimitException("boom");
        var withInner = new CatalogEnumerationLimitException("boom", inner);

        foreach (var exception in new[] { plain, withMessage, withInner })
        {
            exception.Operation.Should().BeEmpty();
            exception.Limit.Should().Be(0);
        }

        withMessage.Message.Should().Be("boom");
        withInner.InnerException.Should().BeSameAs(inner);
    }
}
