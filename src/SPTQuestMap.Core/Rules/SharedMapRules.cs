using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Rules;

/// <summary>
/// Ranks maps by how many quests both profiles could play together there right now —
/// helps two co-op players pick the map that advances both of them the most.
/// </summary>
public static class SharedMapRules
{
    /// <summary>
    /// Builds one <see cref="SharedMapRanking"/> per map that has at least one quest
    /// both profiles can currently act on, ordered by shared count (largest first).
    /// Maps with no shared actionable quest are omitted.
    /// </summary>
    public static IReadOnlyList<SharedMapRanking> BuildRanking(
        IReadOnlyCollection<SharedMapQuestFact> facts,
        int maxQuestsPerMap = 8)
    {
        var rankings = new List<SharedMapRanking>();
        foreach (var group in facts.GroupBy(fact => fact.MapId, StringComparer.Ordinal))
        {
            var shared = group
                .Where(fact => fact.PrimaryActionable && fact.ComparisonActionable)
                .OrderBy(fact => fact.QuestId, StringComparer.Ordinal)
                .ToArray();
            if (shared.Length == 0) continue;

            var mapName = group.First().MapName;
            var questIds = shared.Take(maxQuestsPerMap).Select(fact => fact.QuestId).ToArray();
            rankings.Add(new SharedMapRanking(group.Key, mapName, shared.Length, questIds));
        }

        return rankings
            .OrderByDescending(ranking => ranking.Count)
            .ThenBy(ranking => ranking.MapName, StringComparer.Ordinal)
            .ToArray();
    }
}
