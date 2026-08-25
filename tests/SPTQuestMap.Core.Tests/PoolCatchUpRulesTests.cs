using NUnit.Framework;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Core.Tests;

/// <summary>
/// Catch-up analysis across a pool of more than two profiles. Roles are per chain, not
/// global, which is the property that lets one group answer both "who is catching up to
/// whom" questions without the user declaring sides.
/// </summary>
[TestFixture]
public sealed class PoolCatchUpRulesTests
{
    [Test]
    public void BuildStandings_PicksTheLeaderAndLaggardOfEachChain()
    {
        // The worked example: regdab 1, woulf 3, scramjet 5, bob 6.
        var standings = PoolCatchUpRules.BuildStandings(
        [
            new PoolChainPosition("regdab", "gunsmith", 1),
            new PoolChainPosition("woulf", "gunsmith", 3),
            new PoolChainPosition("scramjet", "gunsmith", 5),
            new PoolChainPosition("bob", "gunsmith", 6),
        ]);

        Assert.That(standings, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(standings[0].LeaderProfileId, Is.EqualTo("bob"));
            Assert.That(standings[0].LaggardProfileId, Is.EqualTo("regdab"));
            Assert.That(standings[0].StepsBehind, Is.EqualTo(5));
        });
    }

    [Test]
    public void BuildStandings_AssignsRolesPerChain_NotGlobally()
    {
        // regdab trails one chain and leads another. A global "who is behind" would be wrong
        // for one of them, which is the whole reason roles are derived per chain.
        var standings = PoolCatchUpRules.BuildStandings(
        [
            new PoolChainPosition("regdab", "gunsmith", 1),
            new PoolChainPosition("scramjet", "gunsmith", 5),
            new PoolChainPosition("regdab", "punisher", 4),
            new PoolChainPosition("scramjet", "punisher", 2),
        ]);

        var gunsmith = standings.Single(standing => standing.ChainId == "gunsmith");
        var punisher = standings.Single(standing => standing.ChainId == "punisher");
        Assert.Multiple(() =>
        {
            Assert.That(gunsmith.LaggardProfileId, Is.EqualTo("regdab"));
            Assert.That(punisher.LaggardProfileId, Is.EqualTo("scramjet"));
        });
    }

    [Test]
    public void BuildStandings_DropsChainsWhereEveryoneIsLevel()
    {
        var standings = PoolCatchUpRules.BuildStandings(
        [
            new PoolChainPosition("regdab", "spa-tour", 3),
            new PoolChainPosition("woulf", "spa-tour", 3),
        ]);

        Assert.That(standings, Is.Empty);
    }

    [Test]
    public void BuildStandings_OrdersByTheBiggestGapFirst()
    {
        var standings = PoolCatchUpRules.BuildStandings(
        [
            new PoolChainPosition("a", "small", 2), new PoolChainPosition("b", "small", 3),
            new PoolChainPosition("a", "big", 0), new PoolChainPosition("b", "big", 7),
        ]);

        Assert.That(standings.Select(standing => standing.ChainId), Is.EqualTo(new[] { "big", "small" }));
    }

    [Test]
    public void BuildMapPriorities_WeightsByHowFarBehind_NotByChainCount()
    {
        // Woods carries one chain whose laggard is 5 back; Customs carries two chains one
        // back each. The deeper hole is the more valuable trip, so Woods must win.
        var standings = PoolCatchUpRules.BuildStandings(
        [
            new PoolChainPosition("regdab", "deep", 0), new PoolChainPosition("lead", "deep", 5),
            new PoolChainPosition("regdab", "shallow-a", 2), new PoolChainPosition("lead", "shallow-a", 3),
            new PoolChainPosition("regdab", "shallow-b", 2), new PoolChainPosition("lead", "shallow-b", 3),
        ]);

        var priorities = PoolCatchUpRules.BuildMapPriorities(standings,
        [
            new PoolQuestLocation("regdab", "deep", "woods", "Woods"),
            new PoolQuestLocation("regdab", "shallow-a", "customs", "Customs"),
            new PoolQuestLocation("regdab", "shallow-b", "customs", "Customs"),
        ]);

        Assert.That(priorities.Select(priority => priority.MapId), Is.EqualTo(new[] { "woods", "customs" }));
        Assert.Multiple(() =>
        {
            Assert.That(priorities[0].Score, Is.EqualTo(5));
            Assert.That(priorities[1].Score, Is.EqualTo(2));
            Assert.That(priorities[1].ChainCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void BuildMapPriorities_CountsAChainOncePerMap()
    {
        // Six quests from one chain on one map must not outweigh several separate chains.
        var standings = PoolCatchUpRules.BuildStandings(
        [
            new PoolChainPosition("regdab", "chain", 0),
            new PoolChainPosition("lead", "chain", 4),
        ]);

        var priorities = PoolCatchUpRules.BuildMapPriorities(standings,
            Enumerable.Range(0, 6).Select(_ => new PoolQuestLocation("regdab", "chain", "woods", "Woods")).ToArray());

        Assert.That(priorities[0].Score, Is.EqualTo(4), "one chain contributes its gap once, not once per quest");
    }

    [Test]
    public void BuildMapPriorities_IgnoresQuestsBelongingToSomeoneNotBehind()
    {
        var standings = PoolCatchUpRules.BuildStandings(
        [
            new PoolChainPosition("regdab", "chain", 0),
            new PoolChainPosition("lead", "chain", 4),
        ]);

        // "lead" is the benchmark for this chain, so their quests are not catch-up work.
        var priorities = PoolCatchUpRules.BuildMapPriorities(standings,
            [new PoolQuestLocation("lead", "chain", "woods", "Woods")]);

        Assert.That(priorities, Is.Empty);
    }
}
