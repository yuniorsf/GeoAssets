using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Shared.Components.Projects;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.Projects;

/// <summary>
/// <see cref="ProjectPanel.Categorize"/> is the pure list-splitting logic behind XD01-145's
/// panel — factored out as a static method so it's directly unit-testable without a Blazor
/// render tree (this repo has no bUnit yet; matches AssetForm.FindAutoLinkCandidate's pattern).
/// </summary>
public class ProjectPanelTests
{
    private static Project General(string name = "General") => new()
    {
        Id = Guid.NewGuid(), Kind = ProjectKind.General, Name = name,
    };

    private static Project UserFork(Guid createdBy, Guid parentId, string name = "Fork") => new()
    {
        Id = Guid.NewGuid(), Kind = ProjectKind.User, CreatedByUserId = createdBy, ParentProjectId = parentId, Name = name,
    };

    [Fact]
    public void Categorize_SeparatesGeneralProjectsFromUserForks()
    {
        var callerId = Guid.NewGuid();
        var general = General();
        var myFork = UserFork(callerId, general.Id);

        var (generalProjects, myForks) = ProjectPanel.Categorize([general, myFork], callerId);

        generalProjects.Should().ContainSingle().Which.Should().BeSameAs(general);
        myForks.Should().ContainSingle().Which.Should().BeSameAs(myFork);
    }

    [Fact]
    public void Categorize_ExcludesOtherUsersForks()
    {
        // Non-leakage: another user's personal fork must never appear as "my forks".
        var callerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var general = General();
        var othersFork = UserFork(otherUserId, general.Id);

        var (_, myForks) = ProjectPanel.Categorize([general, othersFork], callerId);

        myForks.Should().BeEmpty();
    }

    [Fact]
    public void Categorize_MultipleGeneralProjects_AllIncluded()
    {
        // Acceptance criterion: an org can have more than one General Project.
        var callerId = Guid.NewGuid();
        var generalA = General("Team A");
        var generalB = General("Team B");

        var (generalProjects, _) = ProjectPanel.Categorize([generalA, generalB], callerId);

        generalProjects.Should().HaveCount(2);
        generalProjects.Should().Contain([generalA, generalB]);
    }

    [Fact]
    public void Categorize_GeneralProjectWithNoForks_StillListedWithEmptyForksList()
    {
        // Acceptance criterion: a General Project with zero forks must still show up.
        var callerId = Guid.NewGuid();
        var general = General();

        var (generalProjects, myForks) = ProjectPanel.Categorize([general], callerId);

        generalProjects.Should().ContainSingle();
        myForks.Should().BeEmpty();
    }

    [Fact]
    public void Categorize_EmptyList_ReturnsEmptyBothLists()
    {
        var (generalProjects, myForks) = ProjectPanel.Categorize([], Guid.NewGuid());

        generalProjects.Should().BeEmpty();
        myForks.Should().BeEmpty();
    }

    [Fact]
    public void Categorize_CallerOwnsMultipleForksOfDifferentParents_AllIncluded()
    {
        var callerId = Guid.NewGuid();
        var generalA = General("Team A");
        var generalB = General("Team B");
        var forkA = UserFork(callerId, generalA.Id);
        var forkB = UserFork(callerId, generalB.Id);

        var (_, myForks) = ProjectPanel.Categorize([generalA, generalB, forkA, forkB], callerId);

        myForks.Should().HaveCount(2);
        myForks.Should().Contain([forkA, forkB]);
    }
}
