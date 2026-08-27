using NUnit.Framework;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Core.Tests;

/// <summary>
/// Covers <see cref="QuestObjectiveMapRules.OutstandingTaskLocations"/>, the rule behind
/// "which maps does this player still have work on for this quest".
///
/// The shape of these cases comes from a real report: "Needle in a Haystack" spans Ground Zero,
/// Shoreline, Interchange and Streets, and kept appearing as catch-up work on the first three
/// after those legs were finished, because the Coop views counted a quest against every map it
/// mentioned rather than every map it still needed.
/// </summary>
[TestFixture]
public sealed class QuestObjectiveMapRulesTests
{
    private static QuestMapReference Map(string id) => new(id, id, null);

    private static QuestObjectiveDefinition Objective(string id, params string[] mapIds) =>
        new(id, $"Find the item on {string.Join("/", mapIds)}", "FindItem", null, null, 1, null, [], [])
        {
            TaskLocations = mapIds.Select(Map).ToArray(),
        };

    private static QuestGraphNode Quest(params QuestObjectiveDefinition[] objectives) =>
        new("quest-id", "Needle in a Haystack", "", "trader", "Trader", null, "", "Any",
            new QuestLocation("any", "Any", true, null), null, false,
            [], [], objectives, [], [], [], false, null)
        {
            TaskLocation = Map("any"),
        };

    [Test]
    public void NothingCompleted_ReturnsEveryMapTheQuestTouches()
    {
        var quest = Quest(
            Objective("obj-gz", "ground-zero"),
            Objective("obj-sh", "shoreline"),
            Objective("obj-ic", "interchange"),
            Objective("obj-st", "streets"));

        var maps = QuestObjectiveMapRules.OutstandingTaskLocations(quest, []);

        Assert.That(maps.Select(map => map.Id),
            Is.EquivalentTo(new[] { "ground-zero", "shoreline", "interchange", "streets" }));
    }

    [Test]
    public void FinishedLegsDropOut_AndOnlyTheRemainingMapIsReturned()
    {
        var quest = Quest(
            Objective("obj-gz", "ground-zero"),
            Objective("obj-sh", "shoreline"),
            Objective("obj-ic", "interchange"),
            Objective("obj-st", "streets"));

        // Exactly the reported case: every location found except the Streets one.
        var maps = QuestObjectiveMapRules.OutstandingTaskLocations(
            quest, ["obj-gz", "obj-sh", "obj-ic"]);

        Assert.That(maps.Select(map => map.Id), Is.EqualTo(new[] { "streets" }));
    }

    [Test]
    public void AMapStaysWhileAnyObjectiveOnItIsUnfinished()
    {
        var quest = Quest(
            Objective("obj-a", "customs"),
            Objective("obj-b", "customs"));

        var maps = QuestObjectiveMapRules.OutstandingTaskLocations(quest, ["obj-a"]);

        Assert.That(maps.Select(map => map.Id), Is.EqualTo(new[] { "customs" }),
            "One finished objective must not clear a map another objective still needs.");
    }

    [Test]
    public void EveryObjectiveComplete_ContributesToNoMap()
    {
        var quest = Quest(
            Objective("obj-gz", "ground-zero"),
            Objective("obj-st", "streets"));

        var maps = QuestObjectiveMapRules.OutstandingTaskLocations(quest, ["obj-gz", "obj-st"]);

        Assert.That(maps, Is.Empty,
            "A quest waiting only on a hand-in has no raid left to run, so it belongs to no map.");
    }

    [Test]
    public void OneObjectiveSpanningSeveralMapsCountsEachMapOnce()
    {
        var quest = Quest(Objective("obj-multi", "customs", "woods", "customs"));

        var maps = QuestObjectiveMapRules.OutstandingTaskLocations(quest, []);

        Assert.That(maps.Select(map => map.Id), Is.EquivalentTo(new[] { "customs", "woods" }));
    }

    [Test]
    public void QuestWithNoObjectivesFallsBackToItsOwnLocation()
    {
        var quest = Quest();

        var maps = QuestObjectiveMapRules.OutstandingTaskLocations(quest, []);

        Assert.That(maps.Select(map => map.Id), Is.EqualTo(new[] { "any" }));
    }

    [Test]
    public void ObjectivesWithoutAMapContributeNothing()
    {
        var quest = Quest(
            Objective("obj-handover"),
            Objective("obj-st", "streets"));

        var maps = QuestObjectiveMapRules.OutstandingTaskLocations(quest, []);

        Assert.That(maps.Select(map => map.Id), Is.EqualTo(new[] { "streets" }));
    }
}
