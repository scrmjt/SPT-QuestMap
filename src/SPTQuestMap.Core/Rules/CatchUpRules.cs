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
    /// Builds one <see cref="CatchUpChainGap"/> per trader that has quests, ordered by
    /// how far behind the primary profile is (largest gap first). Traders where the
    /// primary profile is level with or ahead of the comparison profile are omitted.
    /// </summary>
    public static IReadOnlyList<CatchUpChainGap> BuildChainGaps(
        IReadOnlyCollection<CatchUpQuestFact> facts,
        int maxQuestsPerChain = 6)
    {
        var chains = new List<CatchUpChainGap>();
        foreach (var group in facts.GroupBy(fact => fact.TraderId, StringComparer.Ordinal))
        {
            var quests = group.ToArray();
            var primaryDepth = MaxCompletedDepth(quests, fact => fact.PrimaryCompleted);
            var comparisonDepth = MaxCompletedDepth(quests, fact => fact.ComparisonCompleted);
            var gap = comparisonDepth - primaryDepth;
            if (gap <= 0) continue;

            var nextQuests = quests
                .Where(fact => !fact.PrimaryCompleted && fact.Depth <= comparisonDepth)
                .OrderBy(fact => fact.Depth)
                .ThenBy(fact => fact.QuestId, StringComparer.Ordinal)
                .Take(maxQuestsPerChain)
                .Select(fact => new CatchUpQuestRecommendation(fact.QuestId, fact.Depth))
                .ToArray();

            chains.Add(new CatchUpChainGap(group.Key, primaryDepth, comparisonDepth, nextQuests));
        }

        return chains
            .OrderByDescending(chain => chain.Gap)
            .ThenBy(chain => chain.TraderId, StringComparer.Ordinal)
            .ToArray();
    }

    private static int MaxCompletedDepth(IReadOnlyCollection<CatchUpQuestFact> quests, Func<CatchUpQuestFact, bool> completed)
    {
        var depths = quests.Where(completed).Select(fact => fact.Depth + 1).ToArray();
        return depths.Length == 0 ? 0 : depths.Max();
    }
}
