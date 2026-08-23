using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Rules;

/// <summary>
/// Ranks trader questlines by how far a "comparison" (leader) profile has progressed
/// beyond a "primary" (catching-up) profile, and picks the next quests the primary
/// profile should play to close that gap, chain-critical quests first.
/// </summary>
public static class CatchUpRules
{
    /// <summary>
    /// Builds one <see cref="CatchUpChainGap"/> per trader that has quests the
    /// comparison profile completed and the primary profile hasn't, ordered by how
    /// many such quests exist (largest gap first). Traders where the primary profile
    /// has matched every comparison-completed quest are omitted.
    /// </summary>
    /// <remarks>
    /// Deliberately per-quest rather than a single "max completed depth" scalar per
    /// trader: two profiles can tie on deepest-completed-quest for a trader while one
    /// is still behind on a different branch of that same trader's tree (e.g. one
    /// profile cleared a deep, unrelated questline while never starting an earlier,
    /// shallower branch the other profile finished) — a depth comparison alone can't
    /// see that gap since it collapses each profile's whole trader progress to one number.
    /// </remarks>
    public static IReadOnlyList<CatchUpChainGap> BuildChainGaps(
        IReadOnlyCollection<CatchUpQuestFact> facts,
        int maxQuestsPerChain = 6)
    {
        var chains = new List<CatchUpChainGap>();
        foreach (var group in facts.GroupBy(fact => fact.TraderId, StringComparer.Ordinal))
        {
            var quests = group.ToArray();
            var missing = quests
                .Where(fact => fact.ComparisonCompleted && !fact.PrimaryCompleted)
                .OrderBy(fact => fact.Depth)
                .ThenBy(fact => fact.QuestId, StringComparer.Ordinal)
                .ToArray();
            if (missing.Length == 0) continue;

            var nextQuests = missing
                .Take(maxQuestsPerChain)
                .Select(fact => new CatchUpQuestRecommendation(fact.QuestId, fact.Depth))
                .ToArray();

            var primaryCompletedCount = quests.Count(fact => fact.PrimaryCompleted);
            var comparisonCompletedCount = quests.Count(fact => fact.ComparisonCompleted);
            chains.Add(new CatchUpChainGap(group.Key, primaryCompletedCount, comparisonCompletedCount, missing.Length, nextQuests));
        }

        return chains
            .OrderByDescending(chain => chain.Gap)
            .ThenBy(chain => chain.TraderId, StringComparer.Ordinal)
            .ToArray();
    }
}
