using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Navigation;
using GeoAssets.Shared.Navigation;
using Xunit;

namespace GeoAssets.Shared.Tests.Navigation;

/// <summary>
/// The Distribuidoras admin menu entry (XD01-301), and the i18n strings this ticket added:
/// every one must exist, non-empty, in all three shipped languages.
/// </summary>
public class AdminIpBillingDistribuidorasMenuItemTests
{
    private static readonly string[] Languages = ["en", "es", "pt"];

    private static string I18nDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GeoAssets.sln"))) dir = dir.Parent;
        dir.Should().NotBeNull("the test must run from inside the repository");
        return Path.Combine(dir!.FullName, "apps", "GeoAssets.Shared", "wwwroot", "i18n");
    }

    private static Dictionary<string, string> Load(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(I18nDirectory(), $"{language}.json")))!;

    private static bool IsAddedByThisTicket(string key) =>
        key.StartsWith("admin.ipBilling.distribuidoras.", StringComparison.Ordinal) ||
        key.StartsWith("admin.ipBilling.inspectionPosting.", StringComparison.Ordinal) ||
        key.StartsWith("admin.ipBilling.municipios.distribuidoraChart", StringComparison.Ordinal);

    [Fact]
    public void MenuItem_SitsUnderIdentity_RightAfterMunicipios_AndRoutesToThePage()
    {
        var item = new AdminIpBillingDistribuidorasMenuItem();
        var municipios = new AdminIpBillingMunicipiosMenuItem();

        item.Id.Should().Be("admin-ip-billing-distribuidoras");
        item.ParentId.Should().Be("identity");
        item.RouteHref.Should().Be("admin/ip-billing/distribuidoras");
        item.LabelKey.Should().Be("admin.ipBilling.distribuidoras.title");
        item.SortOrder.Should().BeGreaterThan(municipios.SortOrder)
            .And.BeLessThan(new AdminIpBillingLevantamentosMenuItem().SortOrder);
    }

    [Fact]
    public void MenuItem_IdDoesNotCollideWithAnyOtherIpBillingMenuItem()
    {
        MenuItemBase[] items =
        [
            new AdminIpBillingMunicipiosMenuItem(),
            new AdminIpBillingDistribuidorasMenuItem(),
            new AdminIpBillingLevantamentosMenuItem(),
            new AdminIpBillingFiscalizacoesMenuItem(),
            new AdminIpBillingOficiosMenuItem(),
            new AdminIpBillingFaturamentoMenuItem(),
            new AdminIpBillingLampReferenceMenuItem(),
        ];

        items.Select(i => i.Id).Should().OnlyHaveUniqueItems();
        items.Select(i => i.SortOrder).Should().OnlyHaveUniqueItems().And.BeInAscendingOrder();
    }

    [Fact]
    public void I18n_EveryKeyThisTicketAdded_ExistsNonEmptyInAllThreeLanguages()
    {
        var byLanguage = Languages.ToDictionary(l => l, Load, StringComparer.Ordinal);
        var added = byLanguage["en"].Keys.Where(IsAddedByThisTicket).ToList();

        added.Should().HaveCountGreaterThan(30);
        added.Should().Contain(new AdminIpBillingDistribuidorasMenuItem().LabelKey);
        foreach (var language in Languages)
        {
            byLanguage[language].Keys.Where(IsAddedByThisTicket).Should().BeEquivalentTo(added, $"{language}.json must carry the same keys");
            byLanguage[language].Where(kv => IsAddedByThisTicket(kv.Key)).Should().OnlyContain(kv => !string.IsNullOrWhiteSpace(kv.Value));
        }
    }

    [Theory]
    [InlineData("admin.ipBilling.fiscalizacoes.uploadHint")]
    [InlineData("admin.ipBilling.oficios.uploadHint")]
    public void I18n_UploadHints_MentionTheTwoOptionalLampColumns(string key)
    {
        foreach (var language in Languages)
        {
            Load(language)[key].Should().Contain("Tipo de Lâmpada").And.Contain("Potência (W)");
        }
    }

    [Theory]
    [InlineData("assetsTable.previous")]
    [InlineData("assetsTable.next")]
    [InlineData("assetsTable.pagerSummary")]
    public void I18n_PagerKeysTheNewTablesReuse_ExistInAllThreeLanguages(string key)
    {
        foreach (var language in Languages)
        {
            Load(language).Should().ContainKey(key);
        }
    }

    [Fact]
    public void I18n_PlaceholdersMatchAcrossLanguages_ForTheKeysThisTicketAdded()
    {
        var english = Load("en");
        foreach (var language in Languages.Skip(1))
        {
            var translated = Load(language);
            foreach (var key in english.Keys.Where(IsAddedByThisTicket))
            {
                translated[key].Contains("{0}", StringComparison.Ordinal)
                    .Should().Be(english[key].Contains("{0}", StringComparison.Ordinal), $"'{key}' in {language}.json");
            }
        }
    }
}
