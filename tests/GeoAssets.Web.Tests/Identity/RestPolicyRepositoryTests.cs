using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Web.Services.Identity.Rest;
using Xunit;

namespace GeoAssets.Web.Tests.Identity;

public class RestPolicyRepositoryTests
{
    private static readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static RestPolicyRepository Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });

    [Fact]
    public async Task GetAllAsync_MapsPoliciesWithRequirements()
    {
        var dto = new PolicyDto(
            Guid.NewGuid(), "CanEditFeatures", "Puede editar activos GIS", PolicyOperator.All,
            [new PolicyRequirementDto(RequirementType.Permission, "features:edit", null)]);
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new List<PolicyDto> { dto }, options: _opts) });
        var sut = Sut(handler);

        var policies = await sut.GetAllAsync();

        var policy = policies.Should().ContainSingle().Subject;
        policy.Id.Should().Be(dto.Id);
        policy.Name.Should().Be("CanEditFeatures");
        policy.Operator.Should().Be(PolicyOperator.All);
        policy.Requirements.Should().ContainSingle(r =>
            r.Type == RequirementType.Permission && r.Value == "features:edit" && r.PolicyId == dto.Id);
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/policies");
    }

    public static IEnumerable<object[]> UnsupportedMethods()
    {
        yield return [Call((IPolicyRepository r) => r.GetByIdAsync(Guid.NewGuid()))];
        yield return [Call((IPolicyRepository r) => r.GetByNameAsync("CanEditFeatures"))];
        yield return [Call((IPolicyRepository r) => r.AddAsync(new AppPolicy()))];
        yield return [Call((IPolicyRepository r) => r.UpdateAsync(new AppPolicy()))];
        yield return [Call((IPolicyRepository r) => r.DeleteAsync(Guid.NewGuid()))];
        yield return [Call((IPolicyRepository r) => r.SaveChangesAsync())];

        static Func<IPolicyRepository, Task> Call(Func<IPolicyRepository, Task> f) => f;
    }

    [Theory]
    [MemberData(nameof(UnsupportedMethods))]
    public async Task Method_WithNoServerEndpoint_ThrowsNotSupportedException(Func<IPolicyRepository, Task> call)
    {
        var sut = Sut(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called.")));

        var act = () => call(sut);

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
