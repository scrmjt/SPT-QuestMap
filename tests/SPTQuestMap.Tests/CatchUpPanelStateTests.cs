using NUnit.Framework;
using SPTQuestMap.Presentation;
using SPTQuestMap.Services;
using CoreObjectiveMapRules = SPTQuestMap.Core.Rules.QuestObjectiveMapRules;

namespace SPTQuestMap.Tests;

/// <summary>
/// Presentation-layer coverage for the Compare Mode catch-up panel: the map filter
/// over trader gaps, and the "best maps to play together" ranking.
/// </summary>
[TestFixture]
public sealed class CatchUpPanelStateTests
{
    /// <summary>A single-value filter selection, for tests written before multi-select.</summary>
    private static IReadOnlySet<string> Only(string id) => new HashSet<string>(new[] { id }, StringComparer.Ordinal);

    private static IReadOnlySet<string> NoFilter => new HashSet<string>(StringComparer.Ordinal);
    [Test]
    public void SharedMapRankings_RanksMapsWhereBothProfilesStillNeedARaid()
    {
        var state = BuildState(
        [
            // Customs: two quests both profiles still have to run.
            Quest("c1", "trader-a", "customs", "Customs", primary: "Available", comparison: "InProgress"),
            Quest("c2", "trader-a", "customs", "Customs", primary: "InProgress", comparison: "Available"),
            // Woods: only one shared, plus one the comparison profile already finished.
            Quest("w1", "trader-a", "woods", "Woods", primary: "Available", comparison: "Available"),
            Quest("w2", "trader-a", "woods", "Woods", primary: "Available", comparison: "Completed"),
        ]);

        Assert.That(state.SharedMapRankings.Select(ranking => ranking.MapName), Is.EqualTo(new[] { "Customs", "Woods" }));
        Assert.Multiple(() =>
        {
            Assert.That(state.SharedMapRankings[0].Count, Is.EqualTo(2));
            Assert.That(state.SharedMapRankings[0].QuestNames, Is.EqualTo(new[] { "c1", "c2" }));
            Assert.That(state.SharedMapRankings[1].Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void SharedMapRankings_ExcludeReadyToFinishBecauseItNeedsNoRaid()
    {
        // Both profiles have every objective done and only need to hand the quest in,
        // so this map is not worth running together.
        var state = BuildState(
        [
            Quest("c1", "trader-a", "customs", "Customs", primary: "ReadyToFinish", comparison: "ReadyToFinish"),
        ]);

        Assert.That(state.SharedMapRankings, Is.Empty);
    }

    [Test]
    public void SharedMapRankings_ExcludeNonRaidablePseudoLocations()
    {
        var state = BuildState(
        [
            OutOfRaidQuest("hideout", "trader-a", primary: "Available", comparison: "Available"),
            Quest("anywhere", "trader-a", CoreObjectiveMapRules.AnyFilterId, "Any map", primary: "Available", comparison: "Available"),
            Quest("transit", "trader-a", CoreObjectiveMapRules.TransitionFilterId, "Transition", primary: "Available", comparison: "Available"),
            Quest("real", "trader-a", "customs", "Customs", primary: "Available", comparison: "Available"),
        ]);

        Assert.That(state.SharedMapRankings.Select(ranking => ranking.MapId), Is.EqualTo(new[] { "customs" }));
    }

    [Test]
    public void SharedMapRankings_CountAQuestOnEveryMapItCanBeWorkedOn()
    {
        var state = BuildState(
        [
            MultiMapQuest("multi", "trader-a", primary: "Available", comparison: "Available",
                ("customs", "Customs"), ("woods", "Woods")),
        ]);

        Assert.That(state.SharedMapRankings.Select(ranking => ranking.MapId), Is.EquivalentTo(new[] { "customs", "woods" }));
        Assert.That(state.SharedMapRankings.All(ranking => ranking.Count == 1), Is.True);
    }

    [Test]
    public void CatchUpMaps_ListEveryLocationTheFilterCanSelect()
    {
        var state = BuildState(
        [
            Quest("c1", "trader-a", "customs", "Customs", primary: "Available", comparison: "Available"),
            Quest("w1", "trader-a", "woods", "Woods", primary: "Available", comparison: "Available"),
        ]);

        Assert.That(state.FilterMaps().Select(map => map.Id), Is.EqualTo(new[] { "customs", "woods" }));
    }

    [Test]
    public void SetMapFilter_NarrowsChainsToThatMapAndClearsBackToEverything()
    {
        var state = BuildState(
        [
            // Comparison profile is ahead on one quest per map, under different traders.
            Quest("c1", "trader-a", "customs", "Customs", primary: "Available", comparison: "Completed"),
            Quest("w1", "trader-b", "woods", "Woods", primary: "Available", comparison: "Completed"),
        ]);

        Assert.That(state.CatchUpChains.Select(chain => chain.TraderId), Is.EquivalentTo(new[] { "trader-a", "trader-b" }));

        state.SetMapFilter(Only("customs"));
        Assert.Multiple(() =>
        {
            Assert.That(state.MapFilter, Is.EquivalentTo(new[] { "customs" }));
            Assert.That(state.CatchUpChains.Select(chain => chain.TraderId), Is.EqualTo(new[] { "trader-a" }));
        });

        state.SetMapFilter(NoFilter);
        Assert.That(state.CatchUpChains.Select(chain => chain.TraderId), Is.EquivalentTo(new[] { "trader-a", "trader-b" }));
    }

    [Test]
    public void SetMapFilter_LeavesTheSharedMapBreakdownIntact()
    {
        // The breakdown is what the filter is chosen from, so filtering must not shrink it.
        var state = BuildState(
        [
            Quest("c1", "trader-a", "customs", "Customs", primary: "Available", comparison: "Available"),
            Quest("w1", "trader-b", "woods", "Woods", primary: "Available", comparison: "Available"),
        ]);
        var before = state.SharedMapRankings.Select(ranking => ranking.MapId).ToArray();

        state.SetMapFilter(Only("customs"));

        Assert.That(state.SharedMapRankings.Select(ranking => ranking.MapId), Is.EqualTo(before));
    }

    [Test]
    public void CatchUpPanelState_SurvivesASettingsRoundTrip()
    {
        var quests = new[]
        {
            Quest("c1", "trader-a", "customs", "Customs", primary: "Available", comparison: "Completed"),
            Quest("w1", "trader-b", "woods", "Woods", primary: "Available", comparison: "Completed"),
        };
        var state = BuildState(quests);
        state.SetCatchUpExpanded(true);
        state.SetMapFilter(Only("customs"));

        // Round-trip through the real localStorage payload, not just the settings record.
        var json = QuestMapSettingsStorage.Serialize(state.CaptureSettings("profile", "en", "comparison", true));
        var restored = BuildState(quests);
        restored.ApplySettings(QuestMapSettingsStorage.Deserialize(json, null)!);

        Assert.Multiple(() =>
        {
            Assert.That(restored.CatchUpExpanded, Is.True);
            Assert.That(restored.MapFilter, Is.EquivalentTo(new[] { "customs" }));
            // The restored filter must actually be applied, not just stored.
            Assert.That(restored.CatchUpChains.Select(chain => chain.TraderId), Is.EqualTo(new[] { "trader-a" }));
        });
    }

    [Test]
    public void CatchUpPanelState_DefaultsSafelyForSettingsSavedBeforeTheseFieldsExisted()
    {
        // Older stored payloads have no catch-up keys at all; they must not throw and
        // must fall back to "collapsed, no map filter" rather than a null filter.
        const string legacyPayload = """{"selectedProfileId":"profile","language":"en","showAllFuture":false,"showFinished":true,"levelEligibleOnly":true,"inProgressExpanded":false,"search":"","traderFilter":""}""";

        var restored = QuestMapSettingsStorage.Deserialize(legacyPayload, null)!;
        var state = BuildState([Quest("c1", "trader-a", "customs", "Customs", primary: "Available", comparison: "Completed")]);
        state.ApplySettings(restored);

        Assert.Multiple(() =>
        {
            Assert.That(state.CatchUpExpanded, Is.False);
            Assert.That(state.MapFilter, Is.Empty);
            Assert.That(state.CatchUpChains, Is.Not.Empty);
        });
    }

    [Test]
    public void MapFilter_IsDroppedWhenTheMapIsAbsentFromTheLoadedProfiles()
    {
        // Restoring a filter for a map this profile pair has no quests on must not
        // leave the panel filtered by an option the dropdown cannot even show.
        var state = BuildState([Quest("c1", "trader-a", "customs", "Customs", primary: "Available", comparison: "Completed")]);
        state.SetMapFilter(Only("customs"));

        state.SetData(
            BuildTopology([Quest("w1", "trader-b", "woods", "Woods", primary: "Available", comparison: "Completed")]),
            BuildProfile("profile", "Primary", [Quest("w1", "trader-b", "woods", "Woods", primary: "Available", comparison: "Completed")], fact => fact.Primary),
            BuildProfile("comparison", "Comparison", [Quest("w1", "trader-b", "woods", "Woods", primary: "Available", comparison: "Completed")], fact => fact.Comparison));

        Assert.Multiple(() =>
        {
            Assert.That(state.MapFilter, Is.Empty);
            Assert.That(state.CatchUpChains.Select(chain => chain.TraderId), Is.EqualTo(new[] { "trader-b" }));
        });
    }

    [Test]
    public void CatchUpModel_IsEmptyOutsideCompareMode()
    {
        var quests = new[] { Quest("c1", "trader-a", "customs", "Customs", primary: "Available", comparison: "Available") };
        var topology = BuildTopology(quests);
        var state = new QuestMapPageState();
        state.SetData(topology, BuildProfile("profile", "Primary", quests, fact => fact.Primary));

        Assert.Multiple(() =>
        {
            Assert.That(state.CompareMode, Is.False);
            // Everything that compares two profiles is empty...
            Assert.That(state.CatchUpChains, Is.Empty);
            Assert.That(state.SharedMapRankings, Is.Empty);
            Assert.That(state.SeriesProgress, Is.Empty);
            // ...but the map filter drives the graph too, so its options still exist.
            Assert.That(state.FilterMaps().Select(map => map.Id), Is.EqualTo(new[] { "customs" }));
        });
    }

    [Test]
    public void SetMapFilter_NarrowsTheGraphNotJustThePanel()
    {
        var state = BuildState(
        [
            Quest("c1", "trader-a", "customs", "Customs", primary: "Available", comparison: "Available"),
            Quest("w1", "trader-b", "woods", "Woods", primary: "Available", comparison: "Available"),
        ]);
        Assert.That(state.VisibleIds, Does.Contain("c1").And.Contain("w1"));

        state.SetMapFilter(Only("customs"));

        Assert.Multiple(() =>
        {
            Assert.That(state.VisibleIds, Does.Contain("c1"));
            Assert.That(state.VisibleIds, Does.Not.Contain("w1"));
        });
    }

    [Test]
    public void SeriesProgress_ShowsEachProfilesFurthestPartForSeriesOnTheSelectedMap()
    {
        var state = BuildState(
        [
            Series("Gunsmith", 1, "trader-a", "customs", "Customs", primary: "Completed", comparison: "Completed"),
            Series("Gunsmith", 2, "trader-a", "customs", "Customs", primary: "Completed", comparison: "Completed"),
            Series("Gunsmith", 3, "trader-a", "customs", "Customs", primary: "Available", comparison: "Completed"),
            // A different series that only appears on Woods.
            Series("Spa Tour", 1, "trader-b", "woods", "Woods", primary: "Completed", comparison: "Available"),
        ]);

        Assert.That(state.SeriesProgress.Select(entry => entry.SeriesName), Is.EquivalentTo(new[] { "Gunsmith", "Spa Tour" }));

        state.SetMapFilter(Only("customs"));

        var gunsmith = state.SeriesProgress.Single();
        Assert.Multiple(() =>
        {
            Assert.That(gunsmith.SeriesName, Is.EqualTo("Gunsmith"));
            Assert.That(gunsmith.TotalParts, Is.EqualTo(3));
            Assert.That(gunsmith.PrimaryFurthest, Is.EqualTo(2));
            Assert.That(gunsmith.ComparisonFurthest, Is.EqualTo(3));
        });
    }

    private static QuestFact Series(string series, int part, string traderId, string mapId, string mapName, string primary, string comparison) =>
        new($"{series}-{part}", traderId, [new QuestMapReferenceDto(mapId, mapName, null)], OutOfRaid: false, primary, comparison)
        { DisplayName = $"{series} - Part {part}" };

    internal sealed record QuestFact(string Id, string TraderId, QuestMapReferenceDto[] Maps, bool OutOfRaid, string Primary, string Comparison)
    {
        /// <summary>Quest name as shown; series grouping is parsed from it. Defaults to the id.</summary>
        public string? DisplayName { get; init; }
    }

    internal static QuestFact Quest(string id, string traderId, string mapId, string mapName, string primary, string comparison) =>
        new(id, traderId, [new QuestMapReferenceDto(mapId, mapName, null)], OutOfRaid: false, primary, comparison);

    internal static QuestFact MultiMapQuest(string id, string traderId, string primary, string comparison, params (string Id, string Name)[] maps) =>
        new(id, traderId, maps.Select(map => new QuestMapReferenceDto(map.Id, map.Name, null)).ToArray(), OutOfRaid: false, primary, comparison);

    private static QuestFact OutOfRaidQuest(string id, string traderId, string primary, string comparison) =>
        new(id, traderId, [], OutOfRaid: true, primary, comparison);

    internal static QuestMapPageState BuildState(QuestFact[] quests)
    {
        var state = new QuestMapPageState();
        state.SetData(
            BuildTopology(quests),
            BuildProfile("profile", "Primary", quests, fact => fact.Primary),
            BuildProfile("comparison", "Comparison", quests, fact => fact.Comparison));
        return state;
    }

    private static QuestTopologyDto BuildTopology(QuestFact[] quests)
    {
        var nodes = quests.Select(fact =>
        {
            var node = new QuestNodeDto(fact.Id, fact.DisplayName ?? fact.Id, string.Empty, fact.TraderId, fact.TraderId, null, string.Empty,
                "Any", new QuestLocationDto("any", null, true, null), null, false, [], [], [], [], []);
            return fact.OutOfRaid ? node : node with { ActualMaps = fact.Maps };
        }).ToArray();

        var traders = quests
            .Select(fact => fact.TraderId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(traderId => new QuestTraderDto(traderId, traderId, null))
            .ToArray();

        return new QuestTopologyDto("version", nodes, [], traders, [], []);
    }

    private static ProfileStateDto BuildProfile(string id, string nickname, QuestFact[] quests, Func<QuestFact, string> displayState)
    {
        var ids = quests.Select(fact => fact.Id).ToArray();
        var states = quests
            .Select(fact => new QuestStateDto(fact.Id, null, displayState(fact), true, true, null, [], null, [], null))
            .ToArray();
        return new ProfileStateDto(id, nickname, "Usec", 10, 0, false, false, states, [], ids, ids);
    }
}
