using System;
using System.Collections.Generic;
using System.Linq;

namespace SPTQuestMap.Core.Rules;

/// <summary>
/// "How many quests must this player still finish before that one opens up?"
///
/// Used to explain why a quest is outstanding for one player and not another: either the
/// other player already finished it, or they are some number of prerequisites away from it.
/// The second case is the one worth a number.
/// </summary>
public static class ChainDistanceRules
{
    /// <summary>
    /// Counts the transitive prerequisites of <paramref name="questId"/> that
    /// <paramref name="completed"/> does not contain. The quest itself is not counted - the
    /// answer is the work standing between the player and it.
    ///
    /// <paramref name="prerequisites"/> maps a quest to the quests it depends on. Cycles are
    /// tolerated: real quest graphs should be acyclic, but a malformed or modded one must not
    /// hang the UI, so visited nodes are never re-expanded.
    /// </summary>
    public static int UnmetPrerequisiteCount(
        string questId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> prerequisites,
        ISet<string> completed)
    {
        if (questId is null) throw new ArgumentNullException(nameof(questId));
        if (prerequisites is null) throw new ArgumentNullException(nameof(prerequisites));
        if (completed is null) throw new ArgumentNullException(nameof(completed));

        var visited = new HashSet<string>(StringComparer.Ordinal) { questId };
        var queue = new Queue<string>();
        Enqueue(questId);

        var unmet = 0;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (completed.Contains(current)) continue;
            unmet++;
            Enqueue(current);
        }

        return unmet;

        void Enqueue(string id)
        {
            if (!prerequisites.TryGetValue(id, out var parents)) return;
            foreach (var parent in parents)
            {
                // A completed prerequisite still gets marked visited so its own ancestors are
                // not walked: finishing a quest settles everything behind it.
                if (visited.Add(parent)) queue.Enqueue(parent);
            }
        }
    }

    /// <summary>
    /// The furthest a player is from any of <paramref name="questIds"/>, and how many of them
    /// they are behind on at all.
    ///
    /// Reports the maximum rather than the sum: summing double counts shared prerequisites and
    /// produces numbers far larger than the actual work, which reads as noise. "Up to N tasks
    /// away" is the honest summary of a set.
    /// </summary>
    public static (int Furthest, int Count) FurthestDistance(
        IEnumerable<string> questIds,
        IReadOnlyDictionary<string, IReadOnlyList<string>> prerequisites,
        ISet<string> completed)
    {
        if (questIds is null) throw new ArgumentNullException(nameof(questIds));

        var furthest = 0;
        var count = 0;
        foreach (var questId in questIds)
        {
            var distance = UnmetPrerequisiteCount(questId, prerequisites, completed);
            if (distance <= 0) continue;
            count++;
            if (distance > furthest) furthest = distance;
        }

        return (furthest, count);
    }
}
