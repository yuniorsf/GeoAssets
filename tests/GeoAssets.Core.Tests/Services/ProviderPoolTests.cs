using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Provider.InMemory;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

public class ProviderPoolTests
{
    private static InMemoryAssetProvider Provider() => new();

    // ── All ───────────────────────────────────────────────────────────────────

    [Fact]
    public void All_NewInstance_ReturnsEmptyList()
    {
        new ProviderPool().All.Should().BeEmpty();
    }

    [Fact]
    public void All_AfterAdd_ContainsAddedEntry()
    {
        var sut = new ProviderPool();
        var entry = sut.Add("A", Provider());
        sut.All.Should().ContainSingle().Which.Should().BeSameAs(entry);
    }

    // ── Active ────────────────────────────────────────────────────────────────

    [Fact]
    public void Active_NoActiveEntry_ThrowsInvalidOperationException()
    {
        var sut = new ProviderPool();
        sut.Add("A", Provider());
        var act = () => sut.Active;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Active_AfterSetActive_ReturnsActiveEntry()
    {
        var sut = new ProviderPool();
        var entry = sut.Add("A", Provider());
        sut.SetActive(entry.Id);
        sut.Active.Should().BeSameAs(entry);
    }

    // ── Add ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Add_ReturnsEntryWithCorrectName()
    {
        new ProviderPool().Add("MyLayer", Provider()).Name.Should().Be("MyLayer");
    }

    [Fact]
    public void Add_ReturnedEntry_IsOpenAndEnabled()
    {
        var entry = new ProviderPool().Add("A", Provider());
        entry.IsOpen.Should().BeTrue();
        entry.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Add_ReturnedEntry_IsNotActive()
    {
        new ProviderPool().Add("A", Provider()).IsActive.Should().BeFalse();
    }

    [Fact]
    public void Add_FiresChanged()
    {
        var sut = new ProviderPool();
        var fired = false;
        sut.Changed += (_, _) => fired = true;
        sut.Add("A", Provider());
        fired.Should().BeTrue();
    }

    // ── SetActive ─────────────────────────────────────────────────────────────

    [Fact]
    public void SetActive_ExistingId_SetsEntryActive()
    {
        var sut = new ProviderPool();
        var entry = sut.Add("A", Provider());
        sut.SetActive(entry.Id);
        entry.IsActive.Should().BeTrue();
    }

    [Fact]
    public void SetActive_ExistingId_OpensAndEnablesEntry()
    {
        var sut = new ProviderPool();
        var entry = sut.Add("A", Provider());
        entry.IsOpen = false;
        entry.IsEnabled = false;
        sut.SetActive(entry.Id);
        entry.IsOpen.Should().BeTrue();
        entry.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void SetActive_ExistingId_OtherEntriesBecomeInactive()
    {
        var sut = new ProviderPool();
        var a = sut.Add("A", Provider());
        var b = sut.Add("B", Provider());
        sut.SetActive(a.Id);
        sut.SetActive(b.Id);
        a.IsActive.Should().BeFalse();
        b.IsActive.Should().BeTrue();
    }

    [Fact]
    public void SetActive_ExistingId_FiresChanged()
    {
        var sut = new ProviderPool();
        var entry = sut.Add("A", Provider());
        var count = 0;
        sut.Changed += (_, _) => count++;
        sut.SetActive(entry.Id);
        count.Should().Be(1);
    }

    [Fact]
    public void SetActive_UnknownId_DoesNotFireChanged()
    {
        var sut = new ProviderPool();
        sut.Add("A", Provider());
        var fired = false;
        sut.Changed += (_, _) => fired = true;
        sut.SetActive(Guid.NewGuid());
        fired.Should().BeFalse();
    }

    [Fact]
    public void SetActive_UnknownId_AllEntriesBecomeInactive()
    {
        var sut = new ProviderPool();
        var entry = sut.Add("A", Provider());
        sut.SetActive(entry.Id);
        sut.SetActive(Guid.NewGuid());
        entry.IsActive.Should().BeFalse();
    }

    // ── Open ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Open_ExistingId_SetsIsOpenTrue()
    {
        var sut = new ProviderPool();
        var entry = sut.Add("A", Provider());
        entry.IsOpen = false;
        sut.Open(entry.Id);
        entry.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void Open_ExistingId_FiresChanged()
    {
        var sut = new ProviderPool();
        var entry = sut.Add("A", Provider());
        var fired = false;
        sut.Changed += (_, _) => fired = true;
        sut.Open(entry.Id);
        fired.Should().BeTrue();
    }

    [Fact]
    public void Open_UnknownId_DoesNotFireChanged()
    {
        var sut = new ProviderPool();
        var fired = false;
        sut.Changed += (_, _) => fired = true;
        sut.Open(Guid.NewGuid());
        fired.Should().BeFalse();
    }

    // ── Close ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Close_ExistingId_SetsIsOpenFalse()
    {
        var sut = new ProviderPool();
        var entry = sut.Add("A", Provider());
        sut.Close(entry.Id);
        entry.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Close_ExistingId_FiresChanged()
    {
        var sut = new ProviderPool();
        var entry = sut.Add("A", Provider());
        var fired = false;
        sut.Changed += (_, _) => fired = true;
        sut.Close(entry.Id);
        fired.Should().BeTrue();
    }

    [Fact]
    public void Close_UnknownId_DoesNotFireChanged()
    {
        var sut = new ProviderPool();
        var fired = false;
        sut.Changed += (_, _) => fired = true;
        sut.Close(Guid.NewGuid());
        fired.Should().BeFalse();
    }

    // ── Enable ────────────────────────────────────────────────────────────────

    [Fact]
    public void Enable_ExistingId_SetsIsEnabledTrue()
    {
        var sut = new ProviderPool();
        var entry = sut.Add("A", Provider());
        entry.IsEnabled = false;
        sut.Enable(entry.Id);
        entry.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Enable_ExistingId_FiresChanged()
    {
        var sut = new ProviderPool();
        var entry = sut.Add("A", Provider());
        var fired = false;
        sut.Changed += (_, _) => fired = true;
        sut.Enable(entry.Id);
        fired.Should().BeTrue();
    }

    [Fact]
    public void Enable_UnknownId_DoesNotFireChanged()
    {
        var sut = new ProviderPool();
        var fired = false;
        sut.Changed += (_, _) => fired = true;
        sut.Enable(Guid.NewGuid());
        fired.Should().BeFalse();
    }

    // ── Disable ───────────────────────────────────────────────────────────────

    [Fact]
    public void Disable_ExistingId_SetsIsEnabledFalse()
    {
        var sut = new ProviderPool();
        var entry = sut.Add("A", Provider());
        sut.Disable(entry.Id);
        entry.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Disable_ExistingId_FiresChanged()
    {
        var sut = new ProviderPool();
        var entry = sut.Add("A", Provider());
        var fired = false;
        sut.Changed += (_, _) => fired = true;
        sut.Disable(entry.Id);
        fired.Should().BeTrue();
    }

    [Fact]
    public void Disable_UnknownId_DoesNotFireChanged()
    {
        var sut = new ProviderPool();
        var fired = false;
        sut.Changed += (_, _) => fired = true;
        sut.Disable(Guid.NewGuid());
        fired.Should().BeFalse();
    }

    // ── Rename ────────────────────────────────────────────────────────────────

    [Fact]
    public void Rename_ExistingId_UpdatesName()
    {
        var sut = new ProviderPool();
        var entry = sut.Add("Old", Provider());
        sut.Rename(entry.Id, "New");
        entry.Name.Should().Be("New");
    }

    [Fact]
    public void Rename_ExistingId_FiresChanged()
    {
        var sut = new ProviderPool();
        var entry = sut.Add("A", Provider());
        var fired = false;
        sut.Changed += (_, _) => fired = true;
        sut.Rename(entry.Id, "B");
        fired.Should().BeTrue();
    }

    [Fact]
    public void Rename_UnknownId_DoesNotFireChanged()
    {
        var sut = new ProviderPool();
        var fired = false;
        sut.Changed += (_, _) => fired = true;
        sut.Rename(Guid.NewGuid(), "X");
        fired.Should().BeFalse();
    }

    // ── Remove ────────────────────────────────────────────────────────────────

    [Fact]
    public void Remove_NonActiveEntry_RemovesFromAll()
    {
        var sut = new ProviderPool();
        var entry = sut.Add("A", Provider());
        sut.Remove(entry.Id);
        sut.All.Should().BeEmpty();
    }

    [Fact]
    public void Remove_NonActiveEntry_FiresChanged()
    {
        var sut = new ProviderPool();
        var entry = sut.Add("A", Provider());
        var fired = false;
        sut.Changed += (_, _) => fired = true;
        sut.Remove(entry.Id);
        fired.Should().BeTrue();
    }

    [Fact]
    public void Remove_ActiveEntry_DoesNotRemove()
    {
        var sut = new ProviderPool();
        var entry = sut.Add("A", Provider());
        sut.SetActive(entry.Id);
        sut.Remove(entry.Id);
        sut.All.Should().ContainSingle();
    }

    [Fact]
    public void Remove_ActiveEntry_DoesNotFireChanged()
    {
        var sut = new ProviderPool();
        var entry = sut.Add("A", Provider());
        sut.SetActive(entry.Id);
        var fired = false;
        sut.Changed += (_, _) => fired = true;
        sut.Remove(entry.Id);
        fired.Should().BeFalse();
    }

    [Fact]
    public void Remove_UnknownId_DoesNotFireChanged()
    {
        var sut = new ProviderPool();
        var fired = false;
        sut.Changed += (_, _) => fired = true;
        sut.Remove(Guid.NewGuid());
        fired.Should().BeFalse();
    }
}
