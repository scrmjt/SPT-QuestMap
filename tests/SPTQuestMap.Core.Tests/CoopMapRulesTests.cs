using NUnit.Framework;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Core.Tests;

/// <summary>
/// "Where should this group raid together?" across any number of profiles: per-profile
/// outstanding counts plus the quests every selected profile still needs.
/// </summary>
[TestFixture]
public sealed class CoopMapRulesTests
{
    private static CoopMapQuestFact Fact(string profile, string quest, string map = "customs", string name = "Customs")
        => new(profile, quest, map, name);

    [Test]
    public void BuildRows_CountsEachProfileAndTheQuestsAllOfThemStillNeed()
    {
        var rows = CoopMapRules.BuildRows(
        [
            Fact("regdab", "q1"), Fact("woulf", "q1"), Fact("scram", "q1"), // all three
            Fact("regdab", "q2"), Fact("woulf", "q2"),                      // only two
            Fact("regdab", "q3"),                                           // only one
        ], ["regdab", "woulf", "scram"]);

        Assert.That(rows, Has.Count.EqualTo(1));
        var row = rows[0];
        Assert.Multiple(() =>
        {
            Assert.That(row.PerProfile.Select(count => count.Count), Is.EqualTo(new[] { 3, 2, 1 }));
            Assert.That(row.SharedByAll, Is.EqualTo(1), "only q1 is needed by every profile");
            Assert.That(row.AnyProfile, Is.EqualTo(3));
        });
    }

    [Test]
    public void BuildRows_KeepsProfileColumnsInTheOrderGiven()
    {
        var rows = CoopMapRules.BuildRows(
            [Fact("b", "q1"), Fact("a", "q1"), Fact("a", "q2")],
            ["a", "b"]);

        Assert.That(rows[0].PerProfile.Select(count => count.ProfileId), Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void BuildRows_ExcludesQuestsAProfileCannotTakeFromTheSharedCount()
    {
        // scram has no fact for q1 at all. The group cannot close it out together, so it is
        // not shared - even though the other two both need it.
        var rows = CoopMapRules.BuildRows(
            [Fact("regdab", "q1"), Fact("woulf", "q1")],
            ["regdab", "woulf", "scram"]);

        Assert.That(rows[0].SharedByAll, Is.Zero);
    }

    [Test]
    public void BuildRows_IgnoresProfilesOutsideTheSelection()
    {
        var rows = CoopMapRules.BuildRows(
            [Fact("regdab", "q1"), Fact("woulf", "q1"), Fact("stranger", "q1")],
            ["regdab", "woulf"]);

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].PerProfile, Has.Count.EqualTo(2));
            Assert.That(rows[0].SharedByAll, Is.EqualTo(1), "the unselected profile must not affect 'all'");
        });
    }

    [Test]
    public void BuildRows_CountsAQuestOncePerProfilePerMap()
    {
        // The same quest arriving twice for one profile on one map must not inflate anything.
        var rows = CoopMapRules.BuildRows(
            [Fact("regdab", "q1"), Fact("regdab", "q1"), Fact("woulf", "q1")],
            ["regdab", "woulf"]);

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].PerProfile[0].Count, Is.EqualTo(1));
            Assert.That(rows[0].SharedByAll, Is.EqualTo(1));
            Assert.That(rows[0].AnyProfile, Is.EqualTo(1));
        });
    }

    [Test]
    public void BuildRows_RanksTheBestSharedMapFirst()
    {
        var rows = CoopMapRules.BuildRows(
        [
            // Woods: one shared. Customs: two shared, so Customs leads despite fewer rows.
            Fact("a", "w1", "woods", "Woods"), Fact("b", "w1", "woods", "Woods"),
            Fact("a", "w2", "woods", "Woods"),
            Fact("a", "c1", "customs", "Customs"), Fact("b", "c1", "customs", "Customs"),
            Fact("a", "c2", "customs", "Customs"), Fact("b", "c2", "customs", "Customs"),
        ], ["a", "b"]);

        Assert.That(rows.Select(row => row.MapId), Is.EqualTo(new[] { "customs", "woods" }));
    }

    [Test]
    public void BuildRows_WithASingleProfile_TreatsEverythingItNeedsAsShared()
    {
        var rows = CoopMapRules.BuildRows([Fact("solo", "q1"), Fact("solo", "q2")], ["solo"]);

        Assert.That(rows[0].SharedByAll, Is.EqualTo(2));
    }

    [Test]
    public void BuildRows_ReportsWhichQuestsOnlyOneProfileStillNeeds()
    {
        var rows = CoopMapRules.BuildRows(
        [
            Fact("regdab", "shared"), Fact("woulf", "shared"),
            Fact("regdab", "onlyRegdab"),
            Fact("woulf", "onlyWoulf"),
        ], ["regdab", "woulf"]);

        var unique = rows[0].UniqueToProfile;
        Assert.Multiple(() =>
        {
            Assert.That(unique.Single(entry => entry.ProfileId == "regdab").QuestIds, Is.EqualTo(new[] { "onlyRegdab" }));
            Assert.That(unique.Single(entry => entry.ProfileId == "woulf").QuestIds, Is.EqualTo(new[] { "onlyWoulf" }));
        });
    }

    [Test]
    public void BuildRows_WithNoProfilesSelected_ReturnsNothing()
    {
        Assert.That(CoopMapRules.BuildRows([Fact("a", "q1")], []), Is.Empty);
    }
}
