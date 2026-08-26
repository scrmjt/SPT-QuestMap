using System;
using System.Collections.Generic;
using System.Linq;

namespace SPTQuestMap.Core.Rules;

/// <summary>One profile still needing a raid on one map for one quest.</summary>
public sealed record CoopMapQuestFact(string ProfileId, string QuestId, string MapId, string MapName);

/// <summary>How many outstanding quests one profile has on one map.</summary>
public sealed record CoopProfileCount(string ProfileId, int Count);

/// <summary>
/// Quests on a map that ONLY this profile still needs. Everyone else either finished them or
/// cannot reach them yet - the ids are returned so the caller can measure which, against the
/// real quest graph.
/// </summary>
public sealed record CoopUniqueQuests(string ProfileId, IReadOnlyList<string> QuestIds);

/// <summary>
/// A map, what each profile still has to do there, and how much of it they would all be
/// closing out on the same raid.
/// </summary>
public sealed record CoopMapRow(
    string MapId,
    string MapName,
    IReadOnlyList<CoopProfileCount> PerProfile,
    int SharedByAll)
{
    /// <summary>Distinct quests anyone in the group still needs here.</summary>
    public int AnyProfile { get; init; }

    /// <summary>Per profile, the quests here that nobody else in the pool still needs.</summary>
    public IReadOnlyList<CoopUniqueQuests> UniqueToProfile { get; init; } = Array.Empty<CoopUniqueQuests>();
}

/// <summary>
/// "Where should this group raid together?" for any number of profiles.
///
/// Deliberately reports one column per profile plus a single all-shared count rather than
/// every subset: the subsets of N profiles are 2^N - 1, which is 15 columns at four players
/// and 31 at five - unreadable next to a dozen map rows, and past three players nobody is
/// choosing a map by which particular pair overlaps.
/// </summary>
public static class CoopMapRules
{
    /// <summary>
    /// Counts per map. <paramref name="profileIds"/> fixes the column order and, importantly,
    /// defines who "all" means: a quest counts as shared only when every one of these profiles
    /// still needs it. A profile that cannot take a quest at all therefore excludes it from the
    /// shared count, which is the honest answer - the group cannot close it out together.
    ///
    /// Facts are de-duplicated per profile and quest, so a quest listed against several maps
    /// contributes once to each of them and never twice to the same one.
    /// </summary>
    public static IReadOnlyList<CoopMapRow> BuildRows(
        IEnumerable<CoopMapQuestFact> facts,
        IReadOnlyList<string> profileIds)
    {
        if (facts is null) throw new ArgumentNullException(nameof(facts));
        if (profileIds is null) throw new ArgumentNullException(nameof(profileIds));
        if (profileIds.Count == 0) return Array.Empty<CoopMapRow>();

        var wanted = new HashSet<string>(profileIds, StringComparer.Ordinal);
        var byMap = facts
            .Where(fact => wanted.Contains(fact.ProfileId))
            .GroupBy(fact => fact.MapId, StringComparer.Ordinal);

        var rows = new List<CoopMapRow>();
        foreach (var group in byMap)
        {
            var distinct = group
                .GroupBy(fact => (fact.ProfileId, fact.QuestId))
                .Select(pair => pair.First())
                .ToArray();

            var perProfile = profileIds
                .Select(profileId => new CoopProfileCount(
                    profileId,
                    distinct.Count(fact => string.Equals(fact.ProfileId, profileId, StringComparison.Ordinal))))
                .ToArray();

            var sharedByAll = distinct
                .GroupBy(fact => fact.QuestId, StringComparer.Ordinal)
                .Count(questGroup => questGroup
                    .Select(fact => fact.ProfileId)
                    .Distinct(StringComparer.Ordinal)
                    .Count() == profileIds.Count);

            var owners = distinct
                .GroupBy(fact => fact.QuestId, StringComparer.Ordinal)
                .ToDictionary(
                    questGroup => questGroup.Key,
                    questGroup => questGroup.Select(fact => fact.ProfileId).Distinct(StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal);

            var unique = profileIds
                .Select(profileId => new CoopUniqueQuests(
                    profileId,
                    owners
                        .Where(pair => pair.Value.Length == 1 && string.Equals(pair.Value[0], profileId, StringComparison.Ordinal))
                        .Select(pair => pair.Key)
                        .OrderBy(questId => questId, StringComparer.Ordinal)
                        .ToArray()))
                .ToArray();

            rows.Add(new CoopMapRow(group.Key, distinct[0].MapName, perProfile, sharedByAll)
            {
                AnyProfile = owners.Count,
                UniqueToProfile = unique,
            });
        }

        // Best map to run together first: the shared count is the reason to pick a map at all,
        // and total outstanding work only breaks ties.
        return rows
            .OrderByDescending(row => row.SharedByAll)
            .ThenByDescending(row => row.AnyProfile)
            .ThenBy(row => row.MapName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
