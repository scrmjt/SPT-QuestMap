using System;
using System.Collections.Generic;
using System.Linq;

namespace SPTQuestMap.Core.Rules;

/// <summary>How far one profile has advanced in one chain.</summary>
public sealed record PoolChainPosition(string ProfileId, string ChainId, int Step);

/// <summary>An actionable catch-up quest a lagging profile could do on a given map.</summary>
public sealed record PoolQuestLocation(string ProfileId, string ChainId, string MapId, string MapName);

/// <summary>
/// Who leads a chain, who trails it, and by how much. Roles are derived per chain rather
/// than declared up front: the same profile can lead one chain and trail another, which is
/// what makes a flat pool work without asking the user to pick sides.
/// </summary>
public sealed record PoolChainStanding(
    string ChainId,
    string LeaderProfileId,
    int LeaderStep,
    string LaggardProfileId,
    int LaggardStep)
{
    public int StepsBehind => LeaderStep - LaggardStep;
}

/// <summary>A map worth raiding, scored by the urgency of the catch-up work sitting on it.</summary>
public sealed record PoolMapPriority(string MapId, string MapName, int Score, int ChainCount);

/// <summary>
/// Catch-up analysis over a pool of any number of profiles.
///
/// The pairwise comparison elsewhere answers "how do these two differ". This answers a
/// different question: across a group, who is furthest behind in each chain, and where
/// should they go to close the gap.
/// </summary>
public static class PoolCatchUpRules
{
    /// <summary>
    /// For each chain, the furthest-ahead profile is the benchmark and the furthest-behind
    /// is the one to prioritise. Chains where everyone is level are dropped: there is no
    /// catching up to do, and reporting them buries the chains that matter.
    ///
    /// Ordered by the size of the gap, so the most urgent chain leads.
    /// </summary>
    public static IReadOnlyList<PoolChainStanding> BuildStandings(IEnumerable<PoolChainPosition> positions)
    {
        if (positions is null) throw new ArgumentNullException(nameof(positions));

        return positions
            .GroupBy(position => position.ChainId, StringComparer.Ordinal)
            .Select(group =>
            {
                // Ties broken by profile id so the result is stable run to run; without it two
                // profiles on the same step would swap roles between refreshes.
                var leader = group
                    .OrderByDescending(position => position.Step)
                    .ThenBy(position => position.ProfileId, StringComparer.Ordinal)
                    .First();
                var laggard = group
                    .OrderBy(position => position.Step)
                    .ThenBy(position => position.ProfileId, StringComparer.Ordinal)
                    .First();
                return new PoolChainStanding(group.Key, leader.ProfileId, leader.Step, laggard.ProfileId, laggard.Step);
            })
            .Where(standing => standing.StepsBehind > 0)
            .OrderByDescending(standing => standing.StepsBehind)
            .ThenBy(standing => standing.ChainId, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Ranks maps by the total urgency of the catch-up work available there: every chain
    /// contributes its steps-behind for each map its laggard can act on.
    ///
    /// So one player five steps back outranks two players one step back — the deeper hole
    /// is the more valuable trip. Counting chains alone would invert that.
    ///
    /// Only the profile that is actually behind in a chain contributes; a quest belonging to
    /// someone already level in that chain is not catch-up work.
    /// </summary>
    public static IReadOnlyList<PoolMapPriority> BuildMapPriorities(
        IEnumerable<PoolChainStanding> standings,
        IEnumerable<PoolQuestLocation> questLocations)
    {
        if (standings is null) throw new ArgumentNullException(nameof(standings));
        if (questLocations is null) throw new ArgumentNullException(nameof(questLocations));

        var behindByChain = standings.ToDictionary(
            standing => standing.ChainId,
            standing => (standing.LaggardProfileId, standing.StepsBehind),
            StringComparer.Ordinal);

        var scored = new Dictionary<string, (string Name, int Score, HashSet<string> Chains)>(StringComparer.Ordinal);
        foreach (var quest in questLocations)
        {
            if (!behindByChain.TryGetValue(quest.ChainId, out var behind)) continue;
            if (!string.Equals(behind.LaggardProfileId, quest.ProfileId, StringComparison.Ordinal)) continue;

            if (!scored.TryGetValue(quest.MapId, out var entry))
            {
                entry = (quest.MapName, 0, new HashSet<string>(StringComparer.Ordinal));
            }

            // A chain scores once per map however many of its quests are there; otherwise a
            // chain with six quests on Woods would swamp five different chains elsewhere.
            if (entry.Chains.Add(quest.ChainId)) entry.Score += behind.StepsBehind;
            scored[quest.MapId] = entry;
        }

        return scored
            .Select(pair => new PoolMapPriority(pair.Key, pair.Value.Name, pair.Value.Score, pair.Value.Chains.Count))
            .Where(priority => priority.Score > 0)
            .OrderByDescending(priority => priority.Score)
            .ThenByDescending(priority => priority.ChainCount)
            .ThenBy(priority => priority.MapName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
