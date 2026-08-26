using NUnit.Framework;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Core.Tests;

/// <summary>
/// How far a player is from a quest: the transitive prerequisites they have not finished.
/// </summary>
[TestFixture]
public sealed class ChainDistanceRulesTests
{
    private static Dictionary<string, IReadOnlyList<string>> Chain() => new(StringComparer.Ordinal)
    {
        // d <- c <- b <- a   (a is first, d is last)
        ["b"] = new[] { "a" },
        ["c"] = new[] { "b" },
        ["d"] = new[] { "c" },
    };

    private static HashSet<string> Done(params string[] ids) => new(ids, StringComparer.Ordinal);

    [Test]
    public void UnmetPrerequisiteCount_CountsOnlyTheWorkStandingInTheWay()
    {
        // Nothing done: reaching "d" needs a, b and c. "d" itself is not counted.
        Assert.That(ChainDistanceRules.UnmetPrerequisiteCount("d", Chain(), Done()), Is.EqualTo(3));
    }

    [Test]
    public void UnmetPrerequisiteCount_ShrinksAsThePlayerProgresses()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ChainDistanceRules.UnmetPrerequisiteCount("d", Chain(), Done("a")), Is.EqualTo(2));
            Assert.That(ChainDistanceRules.UnmetPrerequisiteCount("d", Chain(), Done("a", "b")), Is.EqualTo(1));
            Assert.That(ChainDistanceRules.UnmetPrerequisiteCount("d", Chain(), Done("a", "b", "c")), Is.Zero);
        });
    }

    [Test]
    public void UnmetPrerequisiteCount_StopsWalkingBehindACompletedPrerequisite()
    {
        // Finishing "c" settles everything behind it, even though a and b are not listed done.
        Assert.That(ChainDistanceRules.UnmetPrerequisiteCount("d", Chain(), Done("c")), Is.Zero);
    }

    [Test]
    public void UnmetPrerequisiteCount_CountsASharedPrerequisiteOnce()
    {
        // Two branches both depend on "root"; it is one quest, not two.
        var graph = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["left"] = new[] { "root" },
            ["right"] = new[] { "root" },
            ["top"] = new[] { "left", "right" },
        };

        Assert.That(ChainDistanceRules.UnmetPrerequisiteCount("top", graph, Done()), Is.EqualTo(3));
    }

    [Test]
    public void UnmetPrerequisiteCount_WithNoPrerequisites_IsZero()
    {
        Assert.That(ChainDistanceRules.UnmetPrerequisiteCount("a", Chain(), Done()), Is.Zero);
    }

    [Test]
    public void UnmetPrerequisiteCount_ToleratesACycleInsteadOfHanging()
    {
        // A malformed or modded graph must not spin the UI.
        var cyclic = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["x"] = new[] { "y" },
            ["y"] = new[] { "x" },
        };

        Assert.That(ChainDistanceRules.UnmetPrerequisiteCount("x", cyclic, Done()), Is.EqualTo(1));
    }

    [Test]
    public void UnmetPrerequisites_ListsTheBlockingQuestsClosestFirst()
    {
        // Reaching "d" means a, then b, then c. Listing them in that order makes the tooltip
        // read as a plan rather than an unordered set.
        Assert.That(
            ChainDistanceRules.UnmetPrerequisites(["d"], Chain(), Done()),
            Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public void UnmetPrerequisites_ExcludesWorkAlreadyDoneAndAnythingBehindIt()
    {
        Assert.That(
            ChainDistanceRules.UnmetPrerequisites(["d"], Chain(), Done("b")),
            Is.EqualTo(new[] { "c" }), "b is done, so a is settled too");
    }

    [Test]
    public void UnmetPrerequisites_MergesTheBlockersOfSeveralTargets()
    {
        var graph = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["left"] = new[] { "root" },
            ["right"] = new[] { "mid" },
            ["mid"] = new[] { "root" },
        };

        // "root" blocks both targets but is one quest, listed once.
        Assert.That(
            ChainDistanceRules.UnmetPrerequisites(["left", "right"], graph, Done()),
            Is.EqualTo(new[] { "root", "mid" }));
    }

    [Test]
    public void FurthestDistance_ReportsTheWorstCaseAndHowManyAreBehind()
    {
        var graph = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["near"] = new[] { "a" },
            ["far"] = new[] { "c" },
            ["c"] = new[] { "b" },
            ["b"] = new[] { "a" },
        };

        var (furthest, count) = ChainDistanceRules.FurthestDistance(["near", "far"], graph, Done());

        Assert.Multiple(() =>
        {
            Assert.That(furthest, Is.EqualTo(3), "far needs a, b and c");
            Assert.That(count, Is.EqualTo(2));
        });
    }

    [Test]
    public void FurthestDistance_IgnoresQuestsThePlayerCanAlreadyTake()
    {
        var (furthest, count) = ChainDistanceRules.FurthestDistance(["b"], Chain(), Done("a"));

        Assert.Multiple(() =>
        {
            Assert.That(furthest, Is.Zero);
            Assert.That(count, Is.Zero, "nothing stands in the way, so the player is not behind");
        });
    }
}
