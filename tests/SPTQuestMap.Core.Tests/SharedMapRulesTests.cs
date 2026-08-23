using NUnit.Framework;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Core.Tests;

[TestFixture]
public sealed class SharedMapRulesTests
{
    [Test]
    public void BuildRanking_RanksLargestSharedCountFirstAndOmitsMapsWithNoOverlap()
    {
        var facts = new[]
        {
            // Customs: 3 quests both can play right now.
            new SharedMapQuestFact("q1", "customs", "Customs", PrimaryActionable: true, ComparisonActionable: true),
            new SharedMapQuestFact("q2", "customs", "Customs", PrimaryActionable: true, ComparisonActionable: true),
            new SharedMapQuestFact("q3", "customs", "Customs", PrimaryActionable: true, ComparisonActionable: true),
            // Woods: 1 shared, plus one primary-only that shouldn't count.
            new SharedMapQuestFact("q4", "woods", "Woods", PrimaryActionable: true, ComparisonActionable: true),
            new SharedMapQuestFact("q5", "woods", "Woods", PrimaryActionable: true, ComparisonActionable: false),
            // Reserve: neither can currently act on the only quest there -> omitted.
            new SharedMapQuestFact("q6", "reserve", "Reserve", PrimaryActionable: false, ComparisonActionable: true),
        };

        var rankings = SharedMapRules.BuildRanking(facts);

        Assert.That(rankings.Select(r => r.MapId), Is.EqualTo(new[] { "customs", "woods" }));
        Assert.That(rankings[0].Count, Is.EqualTo(3));
        Assert.That(rankings[0].QuestIds, Is.EqualTo(new[] { "q1", "q2", "q3" }));
        Assert.That(rankings[1].Count, Is.EqualTo(1));
        Assert.That(rankings[1].QuestIds, Is.EqualTo(new[] { "q4" }));
    }

    [Test]
    public void BuildRanking_CapsQuestIdsAtMaxQuestsPerMapButKeepsTrueCount()
    {
        var facts = Enumerable.Range(0, 10)
            .Select(index => new SharedMapQuestFact($"q{index}", "customs", "Customs", PrimaryActionable: true, ComparisonActionable: true))
            .ToArray();

        var rankings = SharedMapRules.BuildRanking(facts, maxQuestsPerMap: 3);

        Assert.That(rankings[0].Count, Is.EqualTo(10));
        Assert.That(rankings[0].QuestIds, Has.Count.EqualTo(3));
    }

    [Test]
    public void BuildRanking_CountsAQuestOnMultipleMapsUnderEachMapSeparately()
    {
        // A quest with objectives on two maps should show up as an independent
        // shared-count contribution on each map it's tied to.
        var facts = new[]
        {
            new SharedMapQuestFact("multi-map-quest", "customs", "Customs", PrimaryActionable: true, ComparisonActionable: true),
            new SharedMapQuestFact("multi-map-quest", "factory", "Factory", PrimaryActionable: true, ComparisonActionable: true),
        };

        var rankings = SharedMapRules.BuildRanking(facts);

        Assert.That(rankings, Has.Count.EqualTo(2));
        Assert.That(rankings.All(r => r.Count == 1), Is.True);
    }
}
