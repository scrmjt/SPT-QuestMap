using NUnit.Framework;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Tests;

/// <summary>
/// The Coop Map Helper pool ("where should this group raid together").
///
/// It asks the same question as the catch-up planner's "best maps" - which maps does this
/// player still have work on - so it needs the same per-objective filtering. A multi-map quest
/// must stop counting against a map once that player has finished the leg there, and because
/// each player finishes different legs, "shared" has to mean a map where they BOTH still have
/// work rather than a map the quest merely mentions.
/// </summary>
[TestFixture]
public sealed class CoopMapHelperTests
{
    private static CoopMapRow RowFor(IReadOnlyList<CoopMapRow> rows, string mapId) =>
        rows.Single(row => row.MapId == mapId);

    private static int CountFor(CoopMapRow row, string profileId) =>
        row.PerProfile.Single(count => count.ProfileId == profileId).Count;

    [Test]
    public void AQuestCountsOnEveryMapWhileNoLegIsDone()
    {
        var quest = CatchUpPanelStateTests.MultiMapQuest(
            "Needle in a Haystack", "mechanic", primary: "InProgress", comparison: "InProgress",
            ("interchange", "Interchange"), ("streets", "Streets of Tarkov"));

        var state = CatchUpPanelStateTests.BuildState([quest]);
        state.SetPoolProfiles([state.Profile!, state.ComparisonProfile!]);

        Assert.Multiple(() =>
        {
            Assert.That(RowFor(state.CoopMapRows, "interchange").SharedByAll, Is.EqualTo(1));
            Assert.That(RowFor(state.CoopMapRows, "streets").SharedByAll, Is.EqualTo(1),
                "Neither player has done either leg, so both maps are shared work.");
        });
    }

    [Test]
    public void AFinishedLegStopsCountingForThatPlayerOnly()
    {
        var quest = CatchUpPanelStateTests.MultiMapQuest(
            "Needle in a Haystack", "mechanic", primary: "InProgress", comparison: "InProgress",
            ("interchange", "Interchange"), ("streets", "Streets of Tarkov"));

        var state = CatchUpPanelStateTests.BuildState(
            [quest with { PrimaryDoneMaps = ["interchange"], ComparisonDoneMaps = ["streets"] }]);
        state.SetPoolProfiles([state.Profile!, state.ComparisonProfile!]);

        var interchange = RowFor(state.CoopMapRows, "interchange");
        var streets = RowFor(state.CoopMapRows, "streets");
        Assert.Multiple(() =>
        {
            Assert.That(CountFor(interchange, "profile"), Is.EqualTo(0),
                "The primary player has already done the Interchange leg.");
            Assert.That(CountFor(interchange, "comparison"), Is.EqualTo(1));
            Assert.That(CountFor(streets, "profile"), Is.EqualTo(1));
            Assert.That(CountFor(streets, "comparison"), Is.EqualTo(0),
                "The comparison player has already done the Streets leg.");

            Assert.That(interchange.SharedByAll, Is.EqualTo(0));
            Assert.That(streets.SharedByAll, Is.EqualTo(0),
                "Neither map is worth running together: on each, only one of them still needs it.");
        });
    }

    [Test]
    public void AMapBothPlayersStillNeedStaysShared()
    {
        var quest = CatchUpPanelStateTests.MultiMapQuest(
            "Needle in a Haystack", "mechanic", primary: "InProgress", comparison: "InProgress",
            ("interchange", "Interchange"), ("streets", "Streets of Tarkov"));

        // Both have cleared Interchange; both still owe Streets.
        var state = CatchUpPanelStateTests.BuildState(
            [quest with { PrimaryDoneMaps = ["interchange"], ComparisonDoneMaps = ["interchange"] }]);
        state.SetPoolProfiles([state.Profile!, state.ComparisonProfile!]);

        Assert.Multiple(() =>
        {
            Assert.That(state.CoopMapRows.Any(row => row.MapId == "interchange"), Is.False,
                "A map nobody still needs should not be offered as a place to raid.");
            Assert.That(RowFor(state.CoopMapRows, "streets").SharedByAll, Is.EqualTo(1));
        });
    }
}
