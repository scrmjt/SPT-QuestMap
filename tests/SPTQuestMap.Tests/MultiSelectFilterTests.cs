using NUnit.Framework;
using SPTQuestMap.Presentation;

namespace SPTQuestMap.Tests;

/// <summary>
/// Coverage for the multi-select trader and map filters. The pre-existing filter tests all
/// exercise a single selection, because that is all the filters used to support; these cover
/// what multi-select added — union semantics, the settings migration from the old
/// single-string fields, and pruning of restored ids the loaded profiles cannot offer.
/// </summary>
[TestFixture]
public sealed class MultiSelectFilterTests
{
    private static IReadOnlySet<string> Set(params string[] ids) => new HashSet<string>(ids, StringComparer.Ordinal);

    [Test]
    public void MapFilter_WithSeveralMapsSelected_IsAUnionNotAnIntersection()
    {
        var state = CatchUpPanelStateTests.BuildState(
        [
            CatchUpPanelStateTests.Quest("c1", "trader-a", "customs", "Customs", "Available", "Completed"),
            CatchUpPanelStateTests.Quest("w1", "trader-b", "woods", "Woods", "Available", "Completed"),
            CatchUpPanelStateTests.Quest("s1", "trader-c", "shoreline", "Shoreline", "Available", "Completed"),
        ]);

        state.SetMapFilter(Set("customs", "woods"));

        // A quest on EITHER selected map qualifies. Treating a multi-selection as an
        // intersection would show nothing at all, since no quest is on two maps here.
        Assert.That(
            state.CatchUpChains.Select(chain => chain.TraderId),
            Is.EquivalentTo(new[] { "trader-a", "trader-b" }));
    }

    [Test]
    public void MapFilter_ClearedBackToEmpty_ShowsEverythingAgain()
    {
        var state = CatchUpPanelStateTests.BuildState(
        [
            CatchUpPanelStateTests.Quest("c1", "trader-a", "customs", "Customs", "Available", "Completed"),
            CatchUpPanelStateTests.Quest("w1", "trader-b", "woods", "Woods", "Available", "Completed"),
        ]);

        state.SetMapFilter(Set("customs"));
        Assert.That(state.CatchUpChains, Has.Count.EqualTo(1));

        state.SetMapFilter(Set());

        Assert.That(state.CatchUpChains.Select(chain => chain.TraderId), Is.EquivalentTo(new[] { "trader-a", "trader-b" }));
    }

    [Test]
    public void SetData_DropsRestoredMapIdsTheLoadedProfilesCannotOffer_ButKeepsTheRest()
    {
        var quests = new[]
        {
            CatchUpPanelStateTests.Quest("c1", "trader-a", "customs", "Customs", "Available", "Completed"),
        };
        var state = CatchUpPanelStateTests.BuildState(quests);

        // "labs" is not a map any loaded quest uses. Keeping it would filter everything away
        // against an option the dropdown cannot even show, stranding the user.
        state.SetMapFilter(Set("customs", "labs"));
        var settings = state.CaptureSettings("profile", "en", "comparison", true);

        var reloaded = CatchUpPanelStateTests.BuildState(quests);
        reloaded.ApplySettings(settings);

        Assert.That(reloaded.MapFilter, Is.EquivalentTo(new[] { "customs" }));
    }

    [Test]
    public void MultiSelection_SurvivesARealSettingsRoundTrip()
    {
        var quests = new[]
        {
            CatchUpPanelStateTests.Quest("c1", "trader-a", "customs", "Customs", "Available", "Completed"),
            CatchUpPanelStateTests.Quest("w1", "trader-b", "woods", "Woods", "Available", "Completed"),
        };
        var state = CatchUpPanelStateTests.BuildState(quests);
        state.SetMapFilter(Set("customs", "woods"));
        state.SetTraderFilter(Set("trader-a", "trader-b"));

        var json = QuestMapSettingsStorage.Serialize(state.CaptureSettings("profile", "en", "comparison", true));
        var restored = CatchUpPanelStateTests.BuildState(quests);
        restored.ApplySettings(QuestMapSettingsStorage.Deserialize(json, null)!);

        Assert.Multiple(() =>
        {
            Assert.That(restored.MapFilter, Is.EquivalentTo(new[] { "customs", "woods" }));
            Assert.That(restored.TraderFilter, Is.EquivalentTo(new[] { "trader-a", "trader-b" }));
        });
    }

    [Test]
    public void SettingsSavedBeforeMultiSelect_PromoteTheirSingleValueRatherThanResetting()
    {
        var quests = new[]
        {
            CatchUpPanelStateTests.Quest("c1", "trader-a", "customs", "Customs", "Available", "Completed"),
            CatchUpPanelStateTests.Quest("w1", "trader-b", "woods", "Woods", "Available", "Completed"),
        };

        // Exactly what an older build wrote: the singular fields, and no plural ones at all.
        // Silently dropping these would reset every existing user's filter on upgrade.
        var legacy = new QuestMapSettings(
            SelectedProfileId: "profile",
            Language: "en",
            ShowAllFuture: false,
            ShowFinished: true,
            LevelEligibleOnly: true,
            InProgressExpanded: false,
            Search: string.Empty,
            TraderFilter: "trader-b",
            Profiles: new Dictionary<string, QuestProfileUiSettings>())
        {
            MapFilter = "woods",
        };

        var state = CatchUpPanelStateTests.BuildState(quests);
        state.ApplySettings(legacy);

        Assert.Multiple(() =>
        {
            Assert.That(state.MapFilter, Is.EquivalentTo(new[] { "woods" }));
            Assert.That(state.TraderFilter, Is.EquivalentTo(new[] { "trader-b" }));
        });
    }

    [Test]
    public void LegacySingularField_IsWrittenOnlyWhenExactlyOneThingIsSelected()
    {
        var quests = new[]
        {
            CatchUpPanelStateTests.Quest("c1", "trader-a", "customs", "Customs", "Available", "Completed"),
            CatchUpPanelStateTests.Quest("w1", "trader-b", "woods", "Woods", "Available", "Completed"),
        };
        var state = CatchUpPanelStateTests.BuildState(quests);

        state.SetMapFilter(Set("customs"));
        Assert.That(state.CaptureSettings("profile", "en").MapFilter, Is.EqualTo("customs"));

        // A wider selection cannot round-trip through a single string. Writing one of them
        // would silently narrow the filter for any older build that reads it, so it writes
        // nothing and that build falls back to "unfiltered" — wrong, but visibly so.
        state.SetMapFilter(Set("customs", "woods"));
        Assert.That(state.CaptureSettings("profile", "en").MapFilter, Is.Empty);
    }

    [Test]
    public void StoredJsonFromAnOlderBuild_StillDeserialises()
    {
        // The regression this guards: changing the existing singular properties to
        // collections instead of adding new ones makes this payload throw JsonException,
        // which the storage layer reports as "no settings" — wiping every saved preference.
        const string olderPayload = """
            {"selectedProfileId":"profile","language":"en","showAllFuture":false,
             "showFinished":true,"levelEligibleOnly":true,"inProgressExpanded":false,
             "search":"","traderFilter":"trader-b","mapFilter":"woods","profiles":{}}
            """;

        var settings = QuestMapSettingsStorage.Deserialize(olderPayload, null);

        Assert.That(settings, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(settings!.TraderFilter, Is.EqualTo("trader-b"));
            Assert.That(settings.MapFilter, Is.EqualTo("woods"));
            Assert.That(settings.TraderFilters, Is.Null);
            Assert.That(settings.MapFilters, Is.Null);
        });
    }
}
