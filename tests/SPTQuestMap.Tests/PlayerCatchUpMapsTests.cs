using NUnit.Framework;
using SPTQuestMap.Presentation;

namespace SPTQuestMap.Tests;

/// <summary>Per-player "best maps to catch up" sections.</summary>
[TestFixture]
public sealed class PlayerCatchUpMapsTests
{
    [Test]
    public void PlayerCatchUpMaps_ListsWhatEachProfileIsBehindOn()
    {
        var state = CatchUpPanelStateTests.BuildState(
        [
            // ScramJet finished it, Woulf has not: Woulf is behind on Customs.
            CatchUpPanelStateTests.Quest("c1", "trader-a", "customs", "Customs", primary: "Available", comparison: "Completed"),
            // The other direction, on Woods.
            CatchUpPanelStateTests.Quest("w1", "trader-b", "woods", "Woods", primary: "Completed", comparison: "Available"),
        ]);

        Assert.That(state.PlayerCatchUpMaps, Has.Count.EqualTo(2), "one section per player");
        var primary = state.PlayerCatchUpMaps[0];
        var comparison = state.PlayerCatchUpMaps[1];
        Assert.Multiple(() =>
        {
            Assert.That(primary.Maps.Select(map => map.MapName), Is.EqualTo(new[] { "Customs" }));
            Assert.That(primary.Maps[0].Quests.Select(quest => quest.Name), Is.EqualTo(new[] { "c1" }));
            Assert.That(comparison.Maps.Select(map => map.MapName), Is.EqualTo(new[] { "Woods" }));
        });
    }

    /// <summary>
    /// The reported case. "Needle in a Haystack" spans Ground Zero, Shoreline, Interchange and
    /// Streets; the player had found every location except the Streets one, yet the quest kept
    /// being listed as catch-up work on all four, because the panel used the quest's whole map
    /// list instead of the maps it still needed.
    /// </summary>
    [Test]
    public void AMapDropsOffOnceItsObjectiveIsDone()
    {
        var quest = CatchUpPanelStateTests.MultiMapQuest(
            "Needle in a Haystack", "mechanic", primary: "InProgress", comparison: "Completed",
            ("ground-zero", "Ground Zero"), ("shoreline", "Shoreline"),
            ("interchange", "Interchange"), ("streets", "Streets of Tarkov"));

        var state = CatchUpPanelStateTests.BuildState(
            [quest with { PrimaryDoneMaps = ["ground-zero", "shoreline", "interchange"] }]);

        var primary = state.PlayerCatchUpMaps[0];
        Assert.That(primary.Maps.Select(map => map.MapName), Is.EqualTo(new[] { "Streets of Tarkov" }),
            "Only the leg still outstanding should count as catch-up work.");
    }

    [Test]
    public void EveryMapStaysWhileNoObjectiveIsDone()
    {
        var quest = CatchUpPanelStateTests.MultiMapQuest(
            "Needle in a Haystack", "mechanic", primary: "Available", comparison: "Completed",
            ("ground-zero", "Ground Zero"), ("streets", "Streets of Tarkov"));

        var state = CatchUpPanelStateTests.BuildState([quest]);

        Assert.That(
            state.PlayerCatchUpMaps[0].Maps.Select(map => map.MapName),
            Is.EquivalentTo(new[] { "Ground Zero", "Streets of Tarkov" }),
            "A quest nobody has started yet is still work on every map it touches.");
    }

    /// <summary>
    /// The two players finish different legs, so "behind on this map" is per player - the same
    /// quest shape can be Streets work for one and Interchange work for the other.
    ///
    /// Two quests rather than one, because a quest only enters a player's catch-up list when
    /// the OTHER player has completed it; a single quest that both players still have in
    /// progress appears in neither, which would make this assertion vacuous.
    /// </summary>
    [Test]
    public void EachPlayersRemainingMapsAreTrackedSeparately()
    {
        var forPrimary = CatchUpPanelStateTests.MultiMapQuest(
            "Needle in a Haystack", "mechanic", primary: "InProgress", comparison: "Completed",
            ("interchange", "Interchange"), ("streets", "Streets of Tarkov"));
        var forComparison = CatchUpPanelStateTests.MultiMapQuest(
            "Hidden Layer", "mechanic", primary: "Completed", comparison: "InProgress",
            ("interchange", "Interchange"), ("streets", "Streets of Tarkov"));

        var state = CatchUpPanelStateTests.BuildState(
        [
            forPrimary with { PrimaryDoneMaps = ["interchange"] },
            forComparison with { ComparisonDoneMaps = ["streets"] },
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(state.PlayerCatchUpMaps[0].Maps.Select(map => map.MapName),
                Is.EqualTo(new[] { "Streets of Tarkov" }),
                "The primary player has already done the Interchange leg.");
            Assert.That(state.PlayerCatchUpMaps[1].Maps.Select(map => map.MapName),
                Is.EqualTo(new[] { "Interchange" }),
                "The comparison player has already done the Streets leg.");
        });
    }
}
