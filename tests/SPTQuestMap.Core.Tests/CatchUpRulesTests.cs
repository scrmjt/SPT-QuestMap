using NUnit.Framework;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Core.Tests;

[TestFixture]
public sealed class CatchUpRulesTests
{
    [Test]
    public void ComputeDepths_AssignsZeroToRootsAndLongestPathToDescendants()
    {
        // a -> b -> d
        //   \-> c ---^
        var depths = QuestChainDepthCalculator.ComputeDepths(
            ["a", "b", "c", "d"],
            [("a", "b"), ("a", "c"), ("b", "d"), ("c", "d")]);

        Assert.Multiple(() =>
        {
            Assert.That(depths["a"], Is.EqualTo(0));
            Assert.That(depths["b"], Is.EqualTo(1));
            Assert.That(depths["c"], Is.EqualTo(1));
            Assert.That(depths["d"], Is.EqualTo(2));
        });
    }

    [Test]
    public void ComputeDepths_IgnoresEdgesToUnknownQuestsAndCycles()
    {
        var depths = QuestChainDepthCalculator.ComputeDepths(
            ["a", "b"],
            [("a", "missing"), ("a", "b"), ("b", "a")]);

        Assert.Multiple(() =>
        {
            Assert.That(depths["a"], Is.EqualTo(0));
            Assert.That(depths.ContainsKey("missing"), Is.False);
        });
    }

    [Test]
    public void BuildChainGaps_RanksLargestGapFirstAndOmitsChainsNotBehind()
    {
        var facts = new[]
        {
            // Trader "prapor": comparison finished depth 3, primary finished depth 0 -> gap 4
            new CatchUpQuestFact("p1", "prapor", 0, PrimaryCompleted: false, ComparisonCompleted: true),
            new CatchUpQuestFact("p2", "prapor", 1, PrimaryCompleted: false, ComparisonCompleted: true),
            new CatchUpQuestFact("p3", "prapor", 2, PrimaryCompleted: false, ComparisonCompleted: true),
            new CatchUpQuestFact("p4", "prapor", 3, PrimaryCompleted: false, ComparisonCompleted: true),

            // Trader "therapist": comparison finished depth 1, primary finished depth 0 -> gap 1
            new CatchUpQuestFact("t1", "therapist", 0, PrimaryCompleted: true, ComparisonCompleted: true),
            new CatchUpQuestFact("t2", "therapist", 1, PrimaryCompleted: false, ComparisonCompleted: true),

            // Trader "skier": primary is ahead -> no gap, excluded entirely
            new CatchUpQuestFact("s1", "skier", 0, PrimaryCompleted: true, ComparisonCompleted: true),
            new CatchUpQuestFact("s2", "skier", 1, PrimaryCompleted: true, ComparisonCompleted: false),
        };

        var chains = CatchUpRules.BuildChainGaps(facts);

        Assert.That(chains.Select(chain => chain.TraderId), Is.EqualTo(new[] { "prapor", "therapist" }));

        var prapor = chains[0];
        Assert.Multiple(() =>
        {
            Assert.That(prapor.Gap, Is.EqualTo(4));
            Assert.That(prapor.NextQuests.Select(quest => quest.QuestId), Is.EqualTo(new[] { "p1", "p2", "p3", "p4" }));
        });

        var therapist = chains[1];
        Assert.Multiple(() =>
        {
            Assert.That(therapist.Gap, Is.EqualTo(1));
            Assert.That(therapist.NextQuests.Select(quest => quest.QuestId), Is.EqualTo(new[] { "t2" }));
        });
    }

    [Test]
    public void BuildChainGaps_DetectsGapHiddenByTiedMaxDepthOnADifferentBranch()
    {
        // Regression test for a real production bug: two profiles both completed a
        // deep, unrelated quest ("shared-deep", depth 5) under the same trader, tying
        // their old "max completed depth" at 6 for that trader. But the comparison
        // profile also completed a much shallower quest on a separate branch
        // ("branch-quest", depth 2) that the primary profile never even started.
        // A depth-scalar comparison sees the tie and reports no gap; per-quest
        // comparison correctly still finds the missing branch-quest.
        var facts = new[]
        {
            new CatchUpQuestFact("shared-deep", "prapor", 5, PrimaryCompleted: true, ComparisonCompleted: true),
            new CatchUpQuestFact("branch-quest", "prapor", 2, PrimaryCompleted: false, ComparisonCompleted: true),
        };

        var chains = CatchUpRules.BuildChainGaps(facts);

        Assert.That(chains, Has.Count.EqualTo(1));
        Assert.That(chains[0].TraderId, Is.EqualTo("prapor"));
        Assert.That(chains[0].Gap, Is.EqualTo(1));
        Assert.That(chains[0].NextQuests.Select(quest => quest.QuestId), Is.EqualTo(new[] { "branch-quest" }));
    }

    [Test]
    public void BuildChainGaps_CapsRecommendationsAtMaxQuestsPerChain()
    {
        var facts = Enumerable.Range(0, 10)
            .Select(index => new CatchUpQuestFact($"q{index}", "prapor", index, PrimaryCompleted: false, ComparisonCompleted: true))
            .ToArray();

        var chains = CatchUpRules.BuildChainGaps(facts, maxQuestsPerChain: 3);

        Assert.That(chains[0].NextQuests, Has.Count.EqualTo(3));
        Assert.That(chains[0].NextQuests.Select(quest => quest.QuestId), Is.EqualTo(new[] { "q0", "q1", "q2" }));
    }
}
