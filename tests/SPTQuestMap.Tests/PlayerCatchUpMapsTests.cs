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
}
