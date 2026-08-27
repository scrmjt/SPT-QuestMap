using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Rules;

public static class QuestObjectiveMapRules
{
    public const string NoLocationFilterId = "no-location";
    public const string AnyFilterId = "any";
    public const string TransitionFilterId = "transition";
    public const string NoLocationBannerUrl = "/files/banners/norvinskzone.png";
    public const string AnyBannerUrl = "/files/banners/67e4047d22d6081b78031ddb.jpg";
    public const string TransitionBannerUrl = "/files/banners/banner_tarkov.png";

    public static IReadOnlyCollection<string> ExpandMapIds(
        IReadOnlyCollection<string> mapIds,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> mapAliases)
    {
        var expanded = mapIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var aliases in mapAliases.Values)
        {
            if (aliases.Any(expanded.Contains)) expanded.UnionWith(aliases);
        }
        return expanded;
    }

    public static bool IsActualMapPlaceholder(QuestLocation location) =>
        location.Any
        || IsTransitionLocation(location.Id, location.Name, location.Any);

    public static bool IsTransitionLocation(
        string locationId,
        string? locationName,
        bool any) =>
        !any
        && (locationId.Contains("transit", StringComparison.OrdinalIgnoreCase)
            || locationId.Contains("marathon", StringComparison.OrdinalIgnoreCase)
            || (locationName?.Contains("transition", StringComparison.OrdinalIgnoreCase) ?? false));

    public static bool ShouldPrependTransitionLabel(
        string locationId,
        string? locationName,
        bool any,
        int derivedMapCount) =>
        derivedMapCount > 0
        && IsTransitionLocation(locationId, locationName, any);

    public static bool UsesActualMaps(QuestGraphNode quest) =>
        quest.ActualMaps.Count > 0;

    public static string NativeFilterId(QuestGraphNode quest) => quest.TaskLocation.Id;

    public static bool MatchesTableLocationFilter(
        QuestGraphNode quest,
        IReadOnlyCollection<string> selectedLocationIds)
    {
        var selected = selectedLocationIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return quest.Objectives.Count == 0
            ? selected.Contains(quest.TaskLocation.Id)
            : quest.Objectives.Any(objective => objective.TaskLocations.Any(map => selected.Contains(map.Id)));
    }

    public static IReadOnlyList<QuestObjectiveDefinition> FilterTableObjectives(
        QuestGraphNode quest,
        IReadOnlyCollection<string>? selectedLocationIds)
    {
        if (selectedLocationIds is null) return quest.Objectives;
        var selected = selectedLocationIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return quest.Objectives
            .Where(objective => objective.TaskLocations.Any(map => selected.Contains(map.Id)))
            .ToArray();
    }

    public static IReadOnlyList<QuestObjectiveDefinition> TableObjectivesExcludedByLocationFilter(
        QuestGraphNode quest,
        IReadOnlyCollection<string>? selectedLocationIds)
    {
        if (selectedLocationIds is null) return [];
        var includedIds = FilterTableObjectives(quest, selectedLocationIds)
            .Select(objective => objective.Id)
            .ToHashSet(StringComparer.Ordinal);
        return quest.Objectives
            .Where(objective => !includedIds.Contains(objective.Id))
            .ToArray();
    }

    /// <summary>
    /// The maps a profile still has work to do on for <paramref name="quest"/>, given the
    /// objectives they have already finished.
    ///
    /// This is deliberately NOT "every map the quest mentions". A quest like "Needle in a
    /// Haystack" spans four maps; once its Ground Zero objective is done, that map has nothing
    /// left for it. Counting a quest against every map it touches made finished maps keep
    /// showing up in the Coop views.
    ///
    /// A quest whose remaining objectives carry no map at all (a pure hand-in) returns nothing,
    /// which is correct - there is no raid to run for it. A quest with no objectives at all
    /// falls back to its own <see cref="QuestGraphNode.TaskLocation"/>.
    /// </summary>
    public static IReadOnlyList<QuestMapReference> OutstandingTaskLocations(
        QuestGraphNode quest,
        IReadOnlyCollection<string> completedObjectiveIds)
    {
        var completed = completedObjectiveIds as ISet<string>
            ?? completedObjectiveIds.ToHashSet(StringComparer.Ordinal);
        var locations = quest.Objectives
            .Where(objective => !completed.Contains(objective.Id))
            .SelectMany(objective => objective.TaskLocations)
            .Where(map => !string.IsNullOrWhiteSpace(map.Id))
            .GroupBy(map => map.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (locations.Length > 0) return locations;
        return quest.Objectives.Count == 0 ? [quest.TaskLocation] : [];
    }

    public static bool IsMapRelated(
        QuestGraphNode quest,
        IReadOnlyCollection<string> currentMapIds)
    {
        var maps = currentMapIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return quest.Objectives.Any(objective => objective.InRaidRelevant
            && IsTaskLocationActiveInRaid(objective.TaskLocations, maps));
    }

    public static bool IsObjectiveActiveInRaid(
        QuestGraphNode quest,
        QuestObjectiveDefinition objective,
        IReadOnlyCollection<string> currentMapIds,
        bool smartTracking)
    {
        var maps = currentMapIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!objective.InRaidRelevant) return !smartTracking && IsMapRelated(quest, maps);
        return IsTaskLocationActiveInRaid(objective.TaskLocations, maps);
    }

    private static bool IsTaskLocationActiveInRaid(
        IReadOnlyCollection<QuestMapReference> taskLocations,
        HashSet<string> currentMapIds) =>
        taskLocations.Any(map =>
            map.Id.Equals(AnyFilterId, StringComparison.OrdinalIgnoreCase)
            || currentMapIds.Contains(map.Id));
}
