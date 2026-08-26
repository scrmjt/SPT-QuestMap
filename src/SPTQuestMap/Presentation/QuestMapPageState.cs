using SPTQuestMap.Services;
using System.Text.Json.Serialization;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;
using CoreQuestGraphRules = SPTQuestMap.Core.Rules.QuestGraphRules;
using QuestTraderOrder = SPTQuestMap.Core.Rules.QuestTraderOrder;

namespace SPTQuestMap.Presentation;

public sealed class QuestMapPageState
{
    private readonly Dictionary<string, QuestProfileUiSettings> _profileSettings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, QuestProfileUiSettings> _comparePairSettings = new(StringComparer.Ordinal);
    private Dictionary<string, QuestNodeDto> _nodeById = new(StringComparer.Ordinal);
    private Dictionary<string, QuestStateDto> _stateById = new(StringComparer.Ordinal);
    private Dictionary<string, QuestStateDto> _comparisonStateById = new(StringComparer.Ordinal);
    private Dictionary<string, TraderStateDto> _traderStateById = new(StringComparer.Ordinal);
    private Dictionary<string, TraderStateDto> _comparisonTraderStateById = new(StringComparer.Ordinal);
    private Dictionary<string, List<IndexedQuestEdge>> _incoming = new(StringComparer.Ordinal);
    private Dictionary<string, List<IndexedQuestEdge>> _outgoing = new(StringComparer.Ordinal);
    private HashSet<string> _applicable = new(StringComparer.Ordinal);
    private HashSet<string> _primaryApplicable = new(StringComparer.Ordinal);
    private HashSet<string> _comparisonApplicable = new(StringComparer.Ordinal);
    private HashSet<string> _repeatableIds = new(StringComparer.Ordinal);
    private Dictionary<string, QuestComparisonDto> _comparisonById = new(StringComparer.Ordinal);
    private readonly HashSet<string> _comparisonScopeIds = new(StringComparer.Ordinal);

    // Every loaded quest's display maps, resolved once per SetData. DisplayMaps
    // allocates per call and is needed by the graph filter, the catch-up panel and the
    // series view, so it must not be recomputed on each filter change.
    private Dictionary<string, QuestMapReferenceDto[]> _nodeMaps = new(StringComparer.Ordinal);

    /// <summary>Quest id to the quests it depends on, for chain-distance walks.</summary>
    private IReadOnlyDictionary<string, IReadOnlyList<string>> _prerequisiteIds =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    // Catch-up inputs that depend only on the loaded topology/profiles, not on the
    // map filter. Cached so changing the filter re-ranks trader gaps without
    // recomputing the quest-graph depths or the shared-map breakdown every time.
    private QuestNodeDto[] _catchUpNodes = [];
    private IReadOnlyDictionary<string, int> _catchUpDepths = new Dictionary<string, int>(StringComparer.Ordinal);
    private Dictionary<string, QuestTraderDto> _catchUpTraders = new(StringComparer.Ordinal);

    public QuestTopologyDto? Topology { get; private set; }
    public ProfileStateDto? Profile { get; private set; }
    public ProfileStateDto? ComparisonProfile { get; private set; }
    public bool CompareMode => ComparisonProfile is not null;
    public bool ShowAllFuture { get; private set; }
    public bool ShowFinished { get; private set; } = true;
    public bool ShowRepeatables { get; private set; } = true;
    /// <summary>
    /// Hide quests whose PMC level gate is above <see cref="LevelCap"/>. Zero means off.
    /// A CAP rather than a per-profile check: in a mixed-level comparison the old
    /// "gated for every profile" test never fired, because a level-38 profile is gated by
    /// almost nothing and kept everything visible.
    /// </summary>
    public int LevelCap { get; private set; }

    public bool LevelEligibleOnly => LevelCap > 0;

    /// <summary>Levels worth offering as presets: every profile currently in play.</summary>
    public IReadOnlyList<(string Label, int Level)> LevelPresets =>
        new[] { (Profile?.Nickname ?? "A", Profile?.Level ?? 0), (ComparisonProfile?.Nickname ?? "B", ComparisonProfile?.Level ?? 0) }
            .Where(preset => preset.Item2 > 0 && (CompareMode || preset.Item1 == (Profile?.Nickname ?? "A")))
            .GroupBy(preset => preset.Item2)
            .Select(group => (Label: string.Join(" / ", group.Select(item => item.Item1)), Level: group.Key))
            .OrderBy(preset => preset.Level)
            .ToArray();
    public bool InProgressExpanded { get; private set; }
    public QuestDetailTextTab DetailTextTab { get; private set; }
    public QuestComparisonFilter ComparisonFilter { get; private set; }
    public string Search { get; private set; } = string.Empty;
    public IReadOnlySet<string> TraderFilter { get; private set; } = new HashSet<string>(StringComparer.Ordinal);
    public string? SelectedId { get; private set; }
    public string? FocusedId { get; private set; }
    public HashSet<string> VisibleIds { get; } = new(StringComparer.Ordinal);
    public HashSet<string> PrerequisiteIds { get; } = new(StringComparer.Ordinal);
    public HashSet<string> SuccessorIds { get; } = new(StringComparer.Ordinal);
    public HashSet<int> HighlightedEdgeIndexes { get; } = [];
    public HashSet<string>? FocusIds { get; private set; }
    public bool FocusMode => FocusedId is not null;
    public bool CanFocusSelection => SelectedId is not null && !_repeatableIds.Contains(SelectedId);
    public QuestNodeDto? SelectedNode => SelectedId is null ? null : GetNode(SelectedId);
    public QuestStateDto? SelectedQuestState => SelectedId is null ? null : GetQuestState(SelectedId);
    public IReadOnlyDictionary<QuestDifferenceCategory, int> ComparisonCounts { get; private set; } = new Dictionary<QuestDifferenceCategory, int>();
    public int ComparableQuestCount { get; private set; }
    public int DifferenceCount { get; private set; }
    public IReadOnlyList<CatchUpChainDto> CatchUpChains { get; private set; } = [];
    public bool CatchUpExpanded { get; private set; }
    public IReadOnlySet<string> MapFilter { get; private set; } = new HashSet<string>(StringComparer.Ordinal);
    public IReadOnlyList<SharedMapRankingDto> SharedMapRankings { get; private set; } = [];

    /// <summary>
    /// Per map: how many quests each profile still needs a raid there for, and how many
    /// both do. Unlike <see cref="SharedMapRankings"/> this covers every location the map
    /// filter can select, including the non-raidable pseudo-locations, because it labels
    /// the filter's own options. Populated outside compare mode too, with zeroed
    /// comparison and shared counts.
    /// </summary>
    public IReadOnlyDictionary<string, MapQuestCounts> MapQuestCounts { get; private set; }
        = new Dictionary<string, MapQuestCounts>(StringComparer.Ordinal);
    public IReadOnlyList<QuestSeriesProgressDto> SeriesProgress { get; private set; } = [];

    /// <summary>Where to raid to close the gap, ranked by actionable catch-up quests.</summary>
    public IReadOnlyList<SharedMapRankingDto> CatchUpMapRankings { get; private set; } = [];

    /// <summary>
    /// Per player, the maps that would best close THEIR gap: the quests the other profile has
    /// finished and they have not, grouped by where they have to be done. One section per
    /// player, because "best map to play together" cannot answer a question that has a
    /// different answer for each of them.
    /// </summary>
    public IReadOnlyList<PlayerCatchUpMapsDto> PlayerCatchUpMaps { get; private set; } = [];

    private void BuildPlayerCatchUpMaps()
    {
        if (!CompareMode || Topology is null || Profile is null || ComparisonProfile is null)
        {
            PlayerCatchUpMaps = [];
            return;
        }

        PlayerCatchUpMaps =
        [
            BuildFor(Profile.ProfileId, PrimaryFilterLabel, GetQuestState, GetComparisonQuestState, _primaryApplicable),
            BuildFor(ComparisonProfile.ProfileId, ComparisonFilterLabel, GetComparisonQuestState, GetQuestState, _comparisonApplicable),
        ];

        PlayerCatchUpMapsDto BuildFor(
            string profileId,
            string label,
            Func<string, QuestStateDto?> own,
            Func<string, QuestStateDto?> other,
            IReadOnlySet<string> applicable)
        {
            var byMap = new Dictionary<string, (string Name, List<string> Quests)>(StringComparer.Ordinal);
            foreach (var node in _catchUpNodes)
            {
                // Behind means: they finished it, this player has not, and this player can
                // actually take it. A quest not applicable to them is not a gap to close.
                if (!applicable.Contains(node.Id)) continue;
                if (other(node.Id)?.DisplayState != "Completed") continue;
                if (own(node.Id)?.DisplayState == "Completed") continue;
                if (!_nodeMaps.TryGetValue(node.Id, out var maps)) continue;

                foreach (var map in maps)
                {
                    if (!IsRaidableMap(map.Id)) continue;
                    if (!byMap.TryGetValue(map.Id, out var entry)) entry = (map.Name, new List<string>());
                    entry.Quests.Add(node.Name);
                    byMap[map.Id] = entry;
                }
            }

            var maps2 = byMap
                .Select(pair => new CatchUpMapEntryDto(
                    pair.Key,
                    pair.Value.Name,
                    pair.Value.Quests.Count,
                    pair.Value.Quests.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray()))
                .OrderByDescending(entry => entry.Count)
                .ThenBy(entry => entry.MapName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            return new PlayerCatchUpMapsDto(profileId, label, maps2);
        }
    }

    /// <summary>
    /// Profiles in the co-op pool. Separate from the pairwise primary/comparison pair on
    /// purpose: the graph tiles and difference categories are inherently two-way, so the pool
    /// feeds the co-op panels only and leaves compare mode untouched.
    /// </summary>
    public IReadOnlyList<ProfileStateDto> PoolProfiles { get; private set; } = [];

    public IReadOnlySet<string> PoolProfileIds { get; private set; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Per map: what each pooled profile still has to raid for, and the overlap.</summary>
    public IReadOnlyList<CoopMapRow> CoopMapRows { get; private set; } = [];

    public void SetPoolProfiles(IReadOnlyList<ProfileStateDto> profiles)
    {
        PoolProfiles = profiles;
        PoolProfileIds = new HashSet<string>(profiles.Select(profile => profile.ProfileId), StringComparer.Ordinal);
        BuildCoopMapRows();
    }

    /// <summary>
    /// Projects the pooled profiles onto <see cref="CoopMapRules"/>. Only raidable locations
    /// take part: "out of raid", "any" and "transition" are not places you can agree to meet,
    /// which is the same exclusion the shared-map ranking makes.
    /// </summary>
    private void BuildCoopMapRows()
    {
        if (Topology is null || PoolProfiles.Count == 0) { CoopMapRows = []; return; }

        var facts = new List<CoopMapQuestFact>();
        foreach (var profile in PoolProfiles)
        {
            var applicable = new HashSet<string>(profile.AllApplicableQuestIds, StringComparer.Ordinal);
            var states = profile.Quests.ToDictionary(quest => quest.QuestId, StringComparer.Ordinal);
            foreach (var questId in applicable)
            {
                if (!NeedsRaid(states.GetValueOrDefault(questId))) continue;
                if (!_nodeMaps.TryGetValue(questId, out var maps)) continue;
                foreach (var map in maps)
                {
                    if (!IsRaidableMap(map.Id)) continue;
                    facts.Add(new CoopMapQuestFact(profile.ProfileId, questId, map.Id, map.Name));
                }
            }
        }

        CoopMapRows = CoopMapRules.BuildRows(facts, PoolProfiles.Select(profile => profile.ProfileId).ToArray());
    }

    /// <summary>
    /// For the quests only <paramref name="profileId"/> still needs on a map, how far each
    /// other pooled profile is from being able to join in.
    ///
    /// A quest is unique to one player for one of two reasons: everybody else finished it, or
    /// they cannot reach it yet. Only the second is reported — someone who is already past a
    /// quest is not "behind" on it, and counting them would invert the meaning.
    /// </summary>
    public IReadOnlyList<CoopBehindDto> CoopBehind(CoopMapRow row, string profileId)
    {
        var unique = row.UniqueToProfile.FirstOrDefault(entry => entry.ProfileId == profileId);
        if (unique is null || unique.QuestIds.Count == 0 || Topology is null) return [];

        var results = new List<CoopBehindDto>();
        foreach (var other in PoolProfiles.Where(profile => profile.ProfileId != profileId))
        {
            var completed = new HashSet<string>(
                other.Quests.Where(quest => quest.DisplayState == "Completed").Select(quest => quest.QuestId),
                StringComparer.Ordinal);
            var (furthest, count) = ChainDistanceRules.FurthestDistance(unique.QuestIds, _prerequisiteIds, completed);
            if (count == 0) continue;
            var blocking = ChainDistanceRules.UnmetPrerequisites(unique.QuestIds, _prerequisiteIds, completed)
                .Select(questId => _nodeById.GetValueOrDefault(questId)?.Name ?? questId)
                .ToArray();
            results.Add(new CoopBehindDto(other.ProfileId, PoolProfileLabel(other.ProfileId), furthest, count, blocking));
        }

        return results
            .OrderByDescending(entry => entry.Furthest)
            .ThenBy(entry => entry.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>Display name for a pooled profile, for column headers.</summary>
    public string PoolProfileLabel(string profileId)
    {
        var profile = PoolProfiles.FirstOrDefault(item => item.ProfileId == profileId);
        return FilterFriendlyName(profile?.Nickname, ProfileLabelRules.ShortLabel(profile?.Nickname, profileId, 5));
    }

    /// <summary>
    /// Compare-mode slot labels: the first two characters of each profile's nickname,
    /// falling back to "A"/"B" when a profile has no usable nickname. These are display
    /// text only — never branch on them to tell the two slots apart, because two profiles
    /// can easily share a prefix ("Scav1"/"Scav2" both yield "Sc").
    /// </summary>
    public string PrimaryShortLabel => ProfileLabelRules.ShortLabel(Profile?.Nickname, "A");

    public string ComparisonShortLabel => ProfileLabelRules.ShortLabel(ComparisonProfile?.Nickname, "B");

    /// <summary>Label for the graph tiles, which have room for about five characters.</summary>
    public string PrimaryTileLabel => ProfileLabelRules.ShortLabel(Profile?.Nickname, "A", 5);

    public string ComparisonTileLabel => ProfileLabelRules.ShortLabel(ComparisonProfile?.Nickname, "B", 5);

    /// <summary>
    /// The nickname when it is short enough to sit in a filter chip, otherwise the tile-sized
    /// abbreviation. Ten characters is what the comparison filter row fits without wrapping.
    /// </summary>
    public string PrimaryFilterLabel => FilterFriendlyName(Profile?.Nickname, PrimaryTileLabel);

    public string ComparisonFilterLabel => FilterFriendlyName(ComparisonProfile?.Nickname, ComparisonTileLabel);

    private static string FilterFriendlyName(string? nickname, string fallback)
    {
        var trimmed = nickname?.Trim();
        return !string.IsNullOrEmpty(trimmed) && trimmed.Length <= 10 ? trimmed : fallback;
    }

    public void ApplySettings(QuestMapSettings settings)
    {
        ShowAllFuture = settings.ShowAllFuture;
        ShowFinished = settings.ShowFinished;
        ShowRepeatables = settings.ShowRepeatables;
        // Older settings stored a boolean; promote it to this profile's own level so the
        // filter keeps meaning the same thing after upgrading.
        LevelCap = settings.LevelCap ?? (settings.LevelEligibleOnly ? Profile?.Level ?? 0 : 0);
        InProgressExpanded = settings.InProgressExpanded;
        DetailTextTab = settings.DetailTextTab;
        Search = settings.Search ?? string.Empty;
        TraderFilter = ReadFilterSet(settings.TraderFilters, settings.TraderFilter);
        ComparisonFilter = settings.ComparisonFilter;
        CatchUpExpanded = settings.CatchUpExpanded;
        MapFilter = ReadFilterSet(settings.MapFilters, settings.MapFilter);
        PruneUnselectableMapFilter();
        _profileSettings.Clear();
        foreach (var pair in settings.Profiles ?? new Dictionary<string, QuestProfileUiSettings>()) _profileSettings[pair.Key] = pair.Value;
        _comparePairSettings.Clear();
        foreach (var pair in settings.ComparePairs ?? new Dictionary<string, QuestProfileUiSettings>()) _comparePairSettings[pair.Key] = pair.Value;
        RestoreCurrentProfileSettings();
        Recalculate();
        // Settings can be applied either side of SetData; re-rank so a restored map
        // filter takes effect when data is already loaded (no-op when it isn't).
        BuildCatchUpChains();
        BuildCatchUpMapRankings();
        BuildPlayerCatchUpMaps();
        BuildSeriesProgress();
    }

    public QuestMapSettings CaptureSettings(string? selectedProfileId, string language, string? comparisonProfileId = null, bool? compareEnabled = null)
    {
        CaptureCurrentProfileSettings();
        return new QuestMapSettings(selectedProfileId, language, ShowAllFuture, ShowFinished, LevelEligibleOnly, InProgressExpanded, Search, SingleOrEmpty(TraderFilter), new Dictionary<string, QuestProfileUiSettings>(_profileSettings, StringComparer.Ordinal))
        {
            ShowRepeatables = ShowRepeatables,
            CompareEnabled = compareEnabled ?? CompareMode,
            ComparisonProfileId = comparisonProfileId ?? ComparisonProfile?.ProfileId,
            ComparisonFilter = ComparisonFilter,
            DetailTextTab = DetailTextTab,
            CatchUpExpanded = CatchUpExpanded,
            MapFilter = SingleOrEmpty(MapFilter),
            LevelCap = LevelCap,
            TraderFilters = TraderFilter.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            MapFilters = MapFilter.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            ComparePairs = new Dictionary<string, QuestProfileUiSettings>(_comparePairSettings, StringComparer.Ordinal),
        };
    }

    public void SetDetailTextTab(QuestDetailTextTab value) => DetailTextTab = value;

    public void SetData(QuestTopologyDto topology, ProfileStateDto? profile, ProfileStateDto? comparisonProfile = null)
    {
        CaptureCurrentProfileSettings();
        Topology = topology;
        Profile = profile;
        ComparisonProfile = comparisonProfile;
        var repeatableEntries = comparisonProfile is null
            ? profile?.RepeatableQuestGroups.SelectMany(group => group.Quests).ToArray() ?? []
            : [];
        _repeatableIds = repeatableEntries.Select(entry => entry.Node.Id).ToHashSet(StringComparer.Ordinal);
        _nodeById = topology.Quests
            .Concat(repeatableEntries.Select(entry => entry.Node))
            .ToDictionary(node => node.Id, StringComparer.Ordinal);
        _stateById = (profile?.Quests ?? [])
            .Concat(repeatableEntries.Select(entry => entry.State))
            .ToDictionary(state => state.QuestId, StringComparer.Ordinal);
        _comparisonStateById = comparisonProfile?.Quests.ToDictionary(state => state.QuestId, StringComparer.Ordinal)
            ?? new Dictionary<string, QuestStateDto>(StringComparer.Ordinal);
        _traderStateById = profile?.Traders.ToDictionary(state => state.TraderId, StringComparer.Ordinal) ?? new Dictionary<string, TraderStateDto>(StringComparer.Ordinal);
        _comparisonTraderStateById = comparisonProfile?.Traders.ToDictionary(state => state.TraderId, StringComparer.Ordinal) ?? new Dictionary<string, TraderStateDto>(StringComparer.Ordinal);
        _primaryApplicable = profile?.AllApplicableQuestIds.Concat(_repeatableIds).ToHashSet(StringComparer.Ordinal) ?? [];
        _comparisonApplicable = comparisonProfile?.AllApplicableQuestIds.ToHashSet(StringComparer.Ordinal) ?? [];
        _applicable = _primaryApplicable.Concat(_comparisonApplicable).ToHashSet(StringComparer.Ordinal);
        _nodeMaps = _nodeById.Values.ToDictionary(
            node => node.Id,
            QuestMapLocationPresentation.DisplayMaps,
            StringComparer.Ordinal);
        // A restored filter can name a map the loaded profiles have no quests on. Drop
        // it rather than filtering everything away against an unselectable option.
        PruneUnselectableMapFilter();
        _prerequisiteIds = topology.Edges
            .GroupBy(edge => edge.TargetId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(edge => edge.SourceId).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        BuildComparisons();
        RebuildCatchUpModel();
        BuildCoopMapRows();
        _incoming = GroupEdges(topology.Edges, edge => edge.TargetId);
        _outgoing = GroupEdges(topology.Edges, edge => edge.SourceId);
        RestoreCurrentProfileSettings();
        Recalculate();
    }

    public void SetShowAllFuture(bool value) { ShowAllFuture = value; Recalculate(); }
    public void SetShowFinished(bool value) { if (!FocusMode) ShowFinished = value; Recalculate(); }
    public void SetShowRepeatables(bool value) { if (!FocusMode) ShowRepeatables = value; Recalculate(); }
    public void SetLevelCap(int value) { if (!FocusMode) LevelCap = value < 0 ? 0 : value; Recalculate(); }
    public void SetInProgressExpanded(bool value) => InProgressExpanded = value;
    public void SetComparisonFilter(QuestComparisonFilter value) { if (!FocusMode && CompareMode) ComparisonFilter = value; Recalculate(); }
    public void SetCatchUpExpanded(bool value) => CatchUpExpanded = value;
    public void SetMapFilter(IReadOnlySet<string>? value)
    {
        if (FocusMode) return;
        MapFilter = Freeze(value);
        Recalculate();
        BuildCatchUpChains();
        BuildCatchUpMapRankings();
        BuildPlayerCatchUpMaps();
        BuildSeriesProgress();
    }
    public void SetSearch(string? value) { Search = value ?? string.Empty; Recalculate(); }
    public void SetTraderFilter(IReadOnlySet<string>? value) { if (!FocusMode) TraderFilter = Freeze(value); Recalculate(); }

    public bool SelectQuest(string? id)
    {
        if (id is null)
        {
            ClearSelection();
            return true;
        }

        if (!_nodeById.ContainsKey(id) || !_applicable.Contains(id)) return false;
        SelectedId = id;
        Recalculate();
        return true;
    }

    public void ClearSelection()
    {
        SelectedId = null;
        FocusedId = null;
        FocusIds = null;
        Recalculate();
    }

    public void ActivateFocus()
    {
        var selectedId = SelectedId;
        if (selectedId is null || _repeatableIds.Contains(selectedId)) return;
        FocusedId = selectedId;
        FocusIds = ComputeChain(selectedId);
        Recalculate();
    }

    public void ClearFocus()
    {
        FocusedId = null;
        FocusIds = null;
        Recalculate();
    }

    public QuestNodeDto? GetNode(string id) => _nodeById.GetValueOrDefault(id);
    public QuestStateDto? GetQuestState(string id) => _stateById.GetValueOrDefault(id);
    public QuestStateDto? GetComparisonQuestState(string id) => _comparisonStateById.GetValueOrDefault(id);
    public TraderStateDto? GetTraderState(string id) => _traderStateById.GetValueOrDefault(id);
    public TraderStateDto? GetComparisonTraderState(string id) => _comparisonTraderStateById.GetValueOrDefault(id);
    public bool IsPrimaryApplicable(string id) => _primaryApplicable.Contains(id);
    public bool IsComparisonApplicable(string id) => _comparisonApplicable.Contains(id);
    public QuestComparisonDto GetComparison(string id) => _comparisonById.GetValueOrDefault(id) ?? QuestComparisonDto.Match(id);
    public bool ObjectiveDiffers(string objectiveId)
    {
        if (!CompareMode || SelectedId is null) return false;
        var primary = GetQuestState(SelectedId)?.Objectives.FirstOrDefault(item => item.ObjectiveId == objectiveId);
        var comparison = GetComparisonQuestState(SelectedId)?.Objectives.FirstOrDefault(item => item.ObjectiveId == objectiveId);
        return !ObjectiveMatches(primary, comparison);
    }
    public IReadOnlyList<IndexedQuestEdge> Incoming(string id) => _incoming.GetValueOrDefault(id) ?? [];
    public IReadOnlyList<IndexedQuestEdge> Outgoing(string id) => _outgoing.GetValueOrDefault(id) ?? [];
    public bool IsCollectorRoute(string id) => Topology?.CollectorPathQuestIds.Contains(id, StringComparer.Ordinal) == true;
    public bool IsLightkeeperRoute(string id) => Topology?.LightkeeperPathQuestIds.Contains(id, StringComparer.Ordinal) == true;

    public IReadOnlyList<QuestTraderDto> FilterTraders()
    {
        if (Topology is null) return [];
        var questTraderIds = _nodeById.Values.Select(quest => quest.TraderId).ToHashSet(StringComparer.Ordinal);
        var ranks = QuestTraderOrder.Ids.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);
        return Topology.Traders
            .Where(trader => questTraderIds.Contains(trader.Id))
            .OrderBy(trader => ranks.GetValueOrDefault(trader.Id, int.MaxValue))
            .ThenBy(trader => trader.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Every location the map filter can select, ordered for display. Includes the
    /// "out of raid", "any" and "transition" pseudo-locations: unlike the shared-map
    /// ranking, which answers "where should we raid", filtering by them is meaningful.
    /// </summary>
    public IReadOnlyList<QuestMapReferenceDto> FilterMaps()
    {
        if (Topology is null) return [];
        return _nodeMaps
            .Where(entry => _applicable.Contains(entry.Key))
            .SelectMany(entry => entry.Value)
            .GroupBy(map => map.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(map => map.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<InProgressTraderGroup> InProgressGroups()
    {
        if (Topology is null || Profile is null) return [];
        var traderMeta = Topology.Traders.ToDictionary(trader => trader.Id, StringComparer.Ordinal);
        var ranks = QuestTraderOrder.Ids.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);
        var ids = Profile.Quests.Where(state => state.DisplayState == "InProgress").Select(state => state.QuestId)
            .Concat(ComparisonProfile?.Quests.Where(state => state.DisplayState == "InProgress").Select(state => state.QuestId) ?? [])
            .Distinct(StringComparer.Ordinal);
        return ids
            .Where(id => _applicable.Contains(id) && _nodeById.ContainsKey(id))
            .Select(id => new InProgressQuest(_nodeById[id], GetQuestState(id), GetComparisonQuestState(id)))
            .GroupBy(item => item.Node.TraderId, StringComparer.Ordinal)
            .Select(group =>
            {
                var trader = traderMeta.GetValueOrDefault(group.Key) ?? new QuestTraderDto(group.Key, group.First().Node.TraderName, group.First().Node.TraderImageUrl);
                return new InProgressTraderGroup(trader, group.OrderBy(item => item.Node.Name, StringComparer.OrdinalIgnoreCase).ToArray());
            })
            .OrderBy(group => ranks.GetValueOrDefault(group.Trader.Id, int.MaxValue))
            .ThenBy(group => group.Trader.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public QuestGraphView BuildGraphView(QuestMapLocalizer localizer) => new(
        Profile?.ProfileId,
        ComparisonProfile?.ProfileId,
        CompareMode,
        localizer.Language,
        localizer.BrowserLocale,
        VisibleIds.ToArray(),
        SelectedId,
        FocusedId,
        PrerequisiteIds.ToArray(),
        SuccessorIds.ToArray(),
        HighlightedEdgeIndexes.ToArray(),
        CompareMode ? VisibleIds.Select(GetComparison).ToArray() : [],
        InProgressExpanded,
        ViewportScope(),
        PrimaryTileLabel,
        ComparisonTileLabel,
        localizer.Strings
    );

    public QuestGraphSnapshot BuildGraphSnapshot(QuestMapLocalizer localizer) => new(
        Topology ?? throw new InvalidOperationException("Quest topology has not been loaded."),
        Profile,
        ComparisonProfile,
        BuildGraphView(localizer)
    );

    public QuestGraphUpdate BuildGraphUpdate() => new(
        VisibleIds.ToArray(), SelectedId, FocusedId, PrerequisiteIds.ToArray(), SuccessorIds.ToArray(),
        HighlightedEdgeIndexes.ToArray(), CompareMode ? VisibleIds.Select(GetComparison).ToArray() : [],
        CompareMode, InProgressExpanded, ViewportScope(), PrimaryTileLabel, ComparisonTileLabel
    );

    private void Recalculate()
    {
        UpdateSelectionHighlight();
        if (FocusedId is not null) FocusIds = ComputeChain(FocusedId);
        VisibleIds.Clear();
        _comparisonScopeIds.Clear();
        if (Profile is null || Topology is null) { UpdateComparisonCounts([]); return; }

        if (!CompareMode)
        {
            var sharedVisible = SPTQuestMap.Core.Rules.QuestVisibilityRules.BuildVisibleIds(
                _nodeById.Values.Select(node => new SPTQuestMap.Core.Rules.QuestVisibilityNode(
                    node.Id, node.Name, node.TraderId, node.TraderName, node.Restartable, _repeatableIds.Contains(node.Id))).ToArray(),
                Topology.Edges.Select(edge => new SPTQuestMap.Core.Models.QuestDependency(edge.SourceId, edge.TargetId)).ToArray(),
                _stateById.Values
                    .Where(state => CoreQuestGraphRules.TryParseProfileDisplayState(state.DisplayState, out _))
                    .Select(state =>
                    {
                        CoreQuestGraphRules.TryParseProfileDisplayState(state.DisplayState, out var display);
                        return new SPTQuestMap.Core.Rules.QuestVisibilityState(
                            state.QuestId,
                            display,
                            state.Blockers.Where(blocker => blocker.Kind == "Prerequisite" && blocker.SubjectId is not null)
                                .Select(blocker => blocker.SubjectId!).Distinct(StringComparer.Ordinal).ToArray());
                    }).ToArray(),
                Profile.DefaultVisibleQuestIds,
                _applicable,
                new SPTQuestMap.Core.Rules.QuestVisibilityOptions(
                    ShowAllFuture,
                    ShowFinished,
                    LevelEligibleOnly,
                    TraderFilter.Count == 1 ? TraderFilter.First() : null,
                    Search,
                    SelectedId,
                    FocusedId,
                    ShowRepeatables));
            VisibleIds.UnionWith(sharedVisible);
            // Applied here rather than inside the shared Core rules: QuestVisibilityNode
            // carries no location data, and the map filter is browser-only — the native
            // client has its own location controls.
            if (!FocusMode && TraderFilter.Count > 1)
            {
                VisibleIds.RemoveWhere(id => id != SelectedId && !PrerequisiteIds.Contains(id) && !MatchesTraderFilter(id));
            }
            if (!FocusMode && MapFilter.Count > 0)
            {
                VisibleIds.RemoveWhere(id => id != SelectedId && !PrerequisiteIds.Contains(id) && !MatchesMapFilter(id));
            }
            UpdateComparisonCounts([]);
            return;
        }

        var baseIds = ShowAllFuture
            ? Profile.AllApplicableQuestIds.Concat(ComparisonProfile?.AllApplicableQuestIds ?? [])
            : Profile.DefaultVisibleQuestIds.Concat(ComparisonProfile?.DefaultVisibleQuestIds ?? []);
        var search = Search.Trim();
        var focusMode = FocusIds is not null;
        var traderUnfiltered = focusMode || TraderFilter.Count == 0;
        var traders = TraderFilter;
        var mapUnfiltered = focusMode || MapFilter.Count == 0;
        var showFinished = focusMode || ShowFinished;
        var levelOnly = !focusMode && LevelEligibleOnly;

        void Consider(string id)
        {
            if (!_nodeById.TryGetValue(id, out var node) || !_applicable.Contains(id) || FocusIds is not null && !FocusIds.Contains(id)) return;
            var state = _stateById.GetValueOrDefault(id);
            var selected = id == SelectedId;
            var revealFinished = focusMode || selected || PrerequisiteIds.Contains(id);
            var selectionContext = selected || PrerequisiteIds.Contains(id) || SuccessorIds.Contains(id);
            if (!showFinished && !revealFinished && IsFinishedForEveryApplicableProfile(node, id)) return;
            if (!selected && levelOnly && ExceedsLevelCap(id)) return;
            if (!selected && !traderUnfiltered && !traders.Contains(node.TraderId) && !PrerequisiteIds.Contains(id)) return;
            if (!selected && !mapUnfiltered && !MatchesMapFilter(node) && !PrerequisiteIds.Contains(id)) return;
            if (!selected && search.Length > 0 && !$"{node.Name} {node.TraderName} {node.Id}".Contains(search, StringComparison.OrdinalIgnoreCase)) return;
            if (CompareMode) _comparisonScopeIds.Add(id);
            if (CompareMode && !focusMode && !selectionContext && !MatchesComparisonFilter(id)) return;
            VisibleIds.Add(id);
        }

        foreach (var id in baseIds) Consider(id);
        if (ShowRepeatables && !CompareMode)
        {
            foreach (var id in _repeatableIds) Consider(id);
        }
        foreach (var id in focusMode ? FocusIds! : PrerequisiteIds) Consider(id);

        if (!focusMode && !traderUnfiltered)
        {
            var visibleTraderQuestIds = VisibleIds
                .Where(id => _nodeById.GetValueOrDefault(id) is { } traderNode
                    && traders.Contains(traderNode.TraderId)
                    && IsBoundaryForAnyApplicableProfile(id))
                .ToArray();

            foreach (var sourceId in visibleTraderQuestIds)
            {
                foreach (var item in Outgoing(sourceId))
                {
                    var targetId = item.Edge.TargetId;
                    if (!_applicable.Contains(targetId)
                        || !_nodeById.ContainsKey(targetId))
                    {
                        continue;
                    }

                    var target = _nodeById[targetId];
                    var state = _stateById.GetValueOrDefault(targetId);
                    var selected = targetId == SelectedId;
                    var revealFinished = selected || PrerequisiteIds.Contains(targetId);
                    if (!showFinished && !revealFinished && IsFinishedForEveryApplicableProfile(target, targetId)) continue;
                    if (!selected && levelOnly && ExceedsLevelCap(targetId)) continue;
                    if (CompareMode) _comparisonScopeIds.Add(targetId);
                    if (CompareMode && !selected && !PrerequisiteIds.Contains(targetId) && !SuccessorIds.Contains(targetId) && !MatchesComparisonFilter(targetId)) continue;

                    VisibleIds.Add(targetId);
                }
            }

            var visibleBlockedQuestIds = VisibleIds
                .Where(id => GetApplicableStates(id).Any(state => state?.Blockers.Any(blocker => blocker.Kind == "Prerequisite") == true))
                .ToArray();

            foreach (var blockedQuestId in visibleBlockedQuestIds)
            {
                var blockers = GetApplicableStates(blockedQuestId)
                    .Where(state => state is not null)
                    .SelectMany(state => state!.Blockers)
                    .Where(blocker => blocker.Kind == "Prerequisite" && blocker.SubjectId is not null)
                    .DistinctBy(blocker => blocker.SubjectId, StringComparer.Ordinal);
                foreach (var blocker in blockers)
                {
                    var prerequisiteId = blocker.SubjectId!;
                    if (_applicable.Contains(prerequisiteId)
                        && _nodeById.ContainsKey(prerequisiteId)
                        && Incoming(blockedQuestId).Any(item => item.Edge.SourceId == prerequisiteId))
                    {
                        VisibleIds.Add(prerequisiteId);
                    }
                }
            }
        }
        UpdateComparisonCounts(_comparisonScopeIds);
    }

    private void UpdateSelectionHighlight()
    {
        PrerequisiteIds.Clear();
        SuccessorIds.Clear();
        HighlightedEdgeIndexes.Clear();
        if (SelectedId is null || !_nodeById.ContainsKey(SelectedId)) return;

        var queue = new Queue<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { SelectedId };
        queue.Enqueue(SelectedId);
        while (queue.TryDequeue(out var current))
        {
            foreach (var item in Incoming(current))
            {
                if (!_applicable.Contains(item.Edge.SourceId)) continue;
                HighlightedEdgeIndexes.Add(item.Index);
                PrerequisiteIds.Add(item.Edge.SourceId);
                if (seen.Add(item.Edge.SourceId)) queue.Enqueue(item.Edge.SourceId);
            }
        }

        foreach (var item in Outgoing(SelectedId))
        {
            if (!_applicable.Contains(item.Edge.TargetId)) continue;
            SuccessorIds.Add(item.Edge.TargetId);
            HighlightedEdgeIndexes.Add(item.Index);
        }
    }

    private HashSet<string> ComputeChain(string id)
    {
        var result = new HashSet<string>(StringComparer.Ordinal) { id };
        var queue = new Queue<string>();
        queue.Enqueue(id);
        while (queue.TryDequeue(out var current))
        {
            foreach (var item in Incoming(current))
            {
                if (_applicable.Contains(item.Edge.SourceId) && result.Add(item.Edge.SourceId)) queue.Enqueue(item.Edge.SourceId);
            }
        }

        foreach (var item in Outgoing(id)) if (_applicable.Contains(item.Edge.TargetId)) result.Add(item.Edge.TargetId);
        return result;
    }

    private static bool IsFinished(QuestNodeDto node, QuestStateDto? state)
    {
        if (state is null) return false;
        return CoreQuestGraphRules.TryParseProfileDisplayState(state.DisplayState, out var display)
            && CoreQuestGraphRules.IsFinishedForFilter(display, node.Restartable, state.Exclusion?.Permanent is not false);
    }

    private bool IsFinishedForEveryApplicableProfile(QuestNodeDto node, string id)
    {
        var states = new List<QuestStateDto?>();
        if (IsPrimaryApplicable(id)) states.Add(GetQuestState(id));
        if (IsComparisonApplicable(id)) states.Add(GetComparisonQuestState(id));
        return states.Count > 0 && states.All(state => IsFinished(node, state));
    }

    /// <summary>
    /// True when the quest's own PMC level requirement is above the cap. Quests with no
    /// level gate are never hidden by this filter.
    /// </summary>
    private bool ExceedsLevelCap(string id)
    {
        if (LevelCap <= 0) return false;
        return RequiredLevel(id) is { } required && required > LevelCap;
    }

    /// <summary>The highest PMC level this quest and its inherited gates demand.</summary>
    public int? RequiredLevel(string id)
    {
        if (!_nodeById.TryGetValue(id, out var node)) return null;
        var levels = node.EffectiveRequirements
            .Where(requirement => requirement.Kind == "Level")
            .Select(requirement => (int)Math.Round(requirement.Value))
            .ToArray();
        return levels.Length == 0 ? null : levels.Max();
    }

    private IEnumerable<QuestStateDto?> GetApplicableStates(string id)
    {
        if (IsPrimaryApplicable(id)) yield return GetQuestState(id);
        if (IsComparisonApplicable(id)) yield return GetComparisonQuestState(id);
    }

    private bool IsBoundaryForAnyApplicableProfile(string id) => GetApplicableStates(id)
        .Any(state => state is not null
            && CoreQuestGraphRules.TryParseProfileDisplayState(state.DisplayState, out var display)
            && CoreQuestGraphRules.IsTraderBoundaryForFilter(display));

    private void BuildComparisons()
    {
        _comparisonById = new Dictionary<string, QuestComparisonDto>(StringComparer.Ordinal);
        if (!CompareMode)
        {
            UpdateComparisonCounts([]);
            return;
        }

        foreach (var id in _applicable)
        {
            var categories = new List<QuestDifferenceCategory>();
            var primaryApplicable = IsPrimaryApplicable(id);
            var comparisonApplicable = IsComparisonApplicable(id);
            if (primaryApplicable && !comparisonApplicable) categories.Add(QuestDifferenceCategory.PrimaryOnly);
            else if (!primaryApplicable && comparisonApplicable) categories.Add(QuestDifferenceCategory.ComparisonOnly);
            else if (primaryApplicable)
            {
                var primary = GetQuestState(id);
                var comparison = GetComparisonQuestState(id);
                if (primary?.Exclusion != comparison?.Exclusion) categories.Add(QuestDifferenceCategory.Exclusion);
                if ((primary?.DisplayState ?? "Locked") != (comparison?.DisplayState ?? "Locked")) categories.Add(QuestDifferenceCategory.Status);
                if (!ObjectivesMatch(primary, comparison)) categories.Add(QuestDifferenceCategory.Objectives);
                if (primary?.AvailableAfter != comparison?.AvailableAfter) categories.Add(QuestDifferenceCategory.AvailableAfter);
            }

            _comparisonById[id] = new QuestComparisonDto(id, categories.ToArray());
        }

        UpdateComparisonCounts(_comparisonById.Keys);
    }

    /// <summary>
    /// True when a quest still requires going into a raid. "ReadyToFinish" is
    /// deliberately excluded: its objectives are already met, so it only needs a
    /// trader hand-in and must not inflate the "play this map together" ranking.
    /// </summary>
    private static bool NeedsRaid(QuestStateDto? state) => state?.DisplayState is "Available" or "InProgress";

    /// <summary>
    /// Pseudo-locations that are not raidable maps. "Out of raid" and "transition" are
    /// category labels rather than places, and an "any map" quest contributes equally
    /// to every map, so none of them help answer "which map should we run together".
    /// </summary>
    private static bool IsRaidableMap(string mapId) =>
        !mapId.Equals(QuestObjectiveMapRules.NoLocationFilterId, StringComparison.OrdinalIgnoreCase)
        && !mapId.Equals(QuestObjectiveMapRules.TransitionFilterId, StringComparison.OrdinalIgnoreCase)
        && !mapId.Equals(QuestObjectiveMapRules.AnyFilterId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Recomputes the catch-up inputs that depend only on the loaded topology and
    /// profiles. Called once per <see cref="SetData"/> so that changing the panel's
    /// map filter does not repeat the quest-graph depth pass or the shared-map scan.
    /// </summary>
    private void RebuildCatchUpModel()
    {
        BuildMapQuestCounts();
        if (!CompareMode || Topology is null)
        {
            _catchUpNodes = [];
            _catchUpDepths = new Dictionary<string, int>(StringComparer.Ordinal);
            _catchUpTraders = new Dictionary<string, QuestTraderDto>(StringComparer.Ordinal);
            CatchUpChains = [];
            SeriesProgress = [];
            SharedMapRankings = [];
            return;
        }

        _catchUpDepths = QuestChainDepthCalculator.ComputeDepths(
            Topology.Quests.Select(node => node.Id).ToArray(),
            Topology.Edges.Select(edge => (edge.SourceId, edge.TargetId)).ToArray());

        _catchUpNodes = Topology.Quests.Where(node => _applicable.Contains(node.Id)).ToArray();
        _catchUpTraders = Topology.Traders.ToDictionary(trader => trader.Id, StringComparer.Ordinal);

        BuildSharedMapRankings();
        BuildCatchUpChains();
        BuildCatchUpMapRankings();
        BuildSeriesProgress();
    }

    /// <summary>
    /// How far each profile has advanced through every named quest series that has a
    /// quest on the selected map (or anywhere, when no map is selected). Positions are
    /// measured over the whole series, not just its quests on that map, so a player's
    /// place in a chain stays comparable as the map filter changes.
    /// </summary>
    private void BuildSeriesProgress()
    {
        if (!CompareMode)
        {
            SeriesProgress = [];
            return;
        }

        var onFilteredMap = _catchUpNodes
            .Where(MatchesMapFilter)
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        // Series membership spans every applicable quest so the denominator is the
        // whole chain; the map filter only decides which series are worth showing.
        var facts = new List<QuestSeriesFact>();
        var seriesOnMap = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in _catchUpNodes)
        {
            if (!QuestSeriesRules.TryParse(node.Name, out var seriesName, out var part)) continue;
            if (onFilteredMap.Contains(node.Id)) seriesOnMap.Add(seriesName);
            facts.Add(new QuestSeriesFact(
                node.Id,
                seriesName,
                part,
                GetQuestState(node.Id)?.DisplayState == "Completed",
                GetComparisonQuestState(node.Id)?.DisplayState == "Completed"));
        }

        var byQuestId = facts.ToLookup(fact => fact.SeriesName, StringComparer.Ordinal);

        SeriesProgress = QuestSeriesRules.BuildProgress(facts)
            .Where(entry => seriesOnMap.Contains(entry.SeriesName))
            // A series both players have reached the same part of says nothing about catching
            // up; listing it buries the ones that do.
            .Where(entry => entry.PrimaryFurthest != entry.ComparisonFurthest)
            .Select(entry =>
            {
                var primaryBehind = entry.PrimaryFurthest < entry.ComparisonFurthest;
                var behindLabel = primaryBehind ? PrimaryFilterLabel : ComparisonFilterLabel;
                var behindFurthest = primaryBehind ? entry.PrimaryFurthest : entry.ComparisonFurthest;
                var leaderFurthest = primaryBehind ? entry.ComparisonFurthest : entry.PrimaryFurthest;

                // The parts between where they are and where the other player got to, that they
                // have not finished. Ordered by part so it reads as the order to play them.
                var catchUp = byQuestId[entry.SeriesName]
                    .Where(fact => fact.Part > behindFurthest && fact.Part <= leaderFurthest)
                    .Where(fact => !(primaryBehind ? fact.PrimaryCompleted : fact.ComparisonCompleted))
                    .OrderBy(fact => fact.Part)
                    .Select(fact => new SeriesCatchUpQuestDto(
                        fact.QuestId,
                        _nodeById.GetValueOrDefault(fact.QuestId)?.Name ?? fact.QuestId,
                        MapNamesFor(fact.QuestId)))
                    .ToArray();

                return new QuestSeriesProgressDto(
                    entry.SeriesName,
                    entry.TotalParts,
                    entry.PrimaryFurthest,
                    entry.ComparisonFurthest)
                {
                    BehindLabel = behindLabel,
                    CatchUpQuests = catchUp,
                };
            })
            .OrderByDescending(entry => entry.CatchUpQuests.Count)
            .ThenBy(entry => entry.SeriesName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// A stable, order-independent key for a filter set. Sets have no defined order, so
    /// hashing them directly would make an identical selection look like a change and
    /// force a redraw.
    /// </summary>
    private static string FilterKey(IReadOnlySet<string> ids)
        => ids.Count == 0 ? string.Empty : string.Join(',', ids.OrderBy(id => id, StringComparer.Ordinal));

    private static IReadOnlySet<string> Freeze(IReadOnlySet<string>? value)
        => value is null || value.Count == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(value, StringComparer.Ordinal);

    /// <summary>
    /// Reads a persisted filter, preferring the multi-select list and falling back to the
    /// single-value field written before multi-select existed. Settings saved by an older
    /// build carry only the singular field, and dropping it would silently reset the user's
    /// filter on first load after upgrading.
    /// </summary>
    private static IReadOnlySet<string> ReadFilterSet(IReadOnlyList<string>? values, string? legacySingle)
    {
        if (values is { Count: > 0 }) return new HashSet<string>(values, StringComparer.Ordinal);
        return string.IsNullOrEmpty(legacySingle)
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>([legacySingle], StringComparer.Ordinal);
    }

    /// <summary>
    /// The value for the pre-multi-select singular settings field. Only a lone selection can
    /// round-trip through it, so a wider selection writes empty rather than silently
    /// narrowing itself for an older build that reads it.
    /// </summary>
    private static string SingleOrEmpty(IReadOnlySet<string> ids) => ids.Count == 1 ? ids.First() : string.Empty;

    /// <summary>
    /// Drops selected maps the loaded profiles have no quests on. A filter restored from
    /// settings can name a map that is not in this profile pair's data at all, and keeping
    /// it would filter everything away against an option the dropdown cannot even show.
    /// Runs from both <see cref="SetData"/> and <see cref="ApplySettings"/> because either
    /// can be the one that first brings a stale filter together with real data.
    /// </summary>
    private void PruneUnselectableMapFilter()
    {
        if (Topology is null || MapFilter.Count == 0) return;
        var selectable = FilterMaps().Select(map => map.Id).ToHashSet(StringComparer.Ordinal);
        if (MapFilter.All(selectable.Contains)) return;
        MapFilter = MapFilter.Where(selectable.Contains).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Counts, per location, the quests each profile still has to raid for. "Still needs a
    /// raid" is the same test the shared-map ranking uses, so a quest whose objectives are
    /// already met and only needs handing in does not inflate the count.
    /// </summary>
    private void BuildMapQuestCounts()
    {
        if (Topology is null)
        {
            MapQuestCounts = new Dictionary<string, MapQuestCounts>(StringComparer.Ordinal);
            return;
        }

        var counts = new Dictionary<string, (int Primary, int Comparison, int Shared)>(StringComparer.Ordinal);
        foreach (var questId in _applicable)
        {
            if (!_nodeMaps.TryGetValue(questId, out var maps)) continue;
            var primary = NeedsRaid(GetQuestState(questId));
            var comparison = CompareMode && NeedsRaid(GetComparisonQuestState(questId));
            if (!primary && !comparison) continue;

            foreach (var map in maps)
            {
                var current = counts.GetValueOrDefault(map.Id);
                counts[map.Id] = (
                    current.Primary + (primary ? 1 : 0),
                    current.Comparison + (comparison ? 1 : 0),
                    current.Shared + (primary && comparison ? 1 : 0));
            }
        }

        MapQuestCounts = counts.ToDictionary(
            pair => pair.Key,
            pair => new MapQuestCounts(pair.Value.Primary, pair.Value.Comparison, pair.Value.Shared),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// What a quest actually asks you to do, as a small ordered list of (kind, amount) pairs
    /// — "kill 5, item 3". The amount is the objective's required value where the template
    /// gives one, otherwise the number of objectives of that kind. Nested objectives that do
    /// not contribute to progress are skipped so a counter is not double counted.
    /// </summary>
    public IReadOnlyList<ObjectiveMixEntry> ObjectiveMix(string questId)
    {
        if (!_nodeById.TryGetValue(questId, out var node) || node.Objectives.Length == 0) return [];

        return node.Objectives
            .Where(objective => objective.ContributesToProgress)
            .GroupBy(objective => objective.ObjectiveKind, StringComparer.Ordinal)
            .Select(group => new ObjectiveMixEntry(group.Key, MixAmount(group.Key, group)))
            .OrderByDescending(entry => entry.Amount)
            .ThenBy(entry => entry.Kind, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// How much of a kind a quest asks for. For most kinds the template's required value IS
    /// the count — kill 8, hand over 12. For loyalty, skill and quest prerequisites it is a
    /// THRESHOLD, not a quantity: summing it turns "reach Loyalty Level 4" into "28 trader".
    /// Those kinds count objectives instead.
    /// </summary>
    private static int MixAmount(string kind, IEnumerable<ObjectiveDefinitionDto> objectives)
    {
        var list = objectives.ToArray();
        if (kind is "trader" or "skill" or "quest") return list.Length;
        return (int)Math.Max(list.Length, list.Sum(objective => objective.RequiredValue ?? 1));
    }

    /// <summary>
    /// A quest the player can actually do something about now: already taken, or offered by
    /// the trader. Everything else is behind a level, prerequisite, trader or prestige gate,
    /// and listing it beside actionable work buries the thing you can actually go do.
    /// </summary>
    private static bool IsActionableState(string? displayState) => displayState is
        "Available" or "AvailableForStart" or "AvailableForFinish" or "InProgress"
        or "Started" or "ReadyToFinish" or "Pending";

    /// <summary>
    /// Locations ranked by how many ACTIONABLE catch-up quests sit on them - "where should I
    /// go to close the gap". Distinct from <see cref="SharedMapRankings"/>, which answers the
    /// different question of where both players still have work.
    /// </summary>
    private void BuildCatchUpMapRankings()
    {
        if (!CompareMode || Topology is null) { CatchUpMapRankings = []; return; }

        var counts = new Dictionary<string, (string Name, List<string> Quests)>(StringComparer.Ordinal);
        foreach (var chain in CatchUpChains)
        {
            foreach (var quest in chain.NextQuests)
            {
                if (!_nodeMaps.TryGetValue(quest.QuestId, out var maps)) continue;
                foreach (var map in maps)
                {
                    if (!IsRaidableMap(map.Id)) continue;
                    if (!counts.TryGetValue(map.Id, out var entry)) entry = (map.Name, new List<string>());
                    entry.Quests.Add(quest.Name);
                    counts[map.Id] = entry;
                }
            }
        }

        CatchUpMapRankings = counts
            .Select(pair => new SharedMapRankingDto(pair.Key, pair.Value.Name, null, pair.Value.Quests.Count, pair.Value.Quests))
            .OrderByDescending(ranking => ranking.Count)
            .ThenBy(ranking => ranking.MapName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>Where a quest can be worked, for the "(Customs, Woods)" suffix in a list.</summary>
    private string MapNamesFor(string questId)
        => _nodeMaps.TryGetValue(questId, out var maps) && maps.Length > 0
            ? string.Join(", ", maps.Take(3).Select(map => map.Name))
            : string.Empty;

    /// <summary>True when no trader is selected, or the quest belongs to one of them.</summary>
    private bool MatchesTraderFilter(string questId)
        => TraderFilter.Count == 0
        || (_nodeById.TryGetValue(questId, out var node) && TraderFilter.Contains(node.TraderId));

    /// <summary>True when no map is selected, or the quest can be worked on that map.</summary>
    private bool MatchesMapFilter(QuestNodeDto node) => MatchesMapFilter(node.Id);

    private bool MatchesMapFilter(string questId) =>
        MapFilter.Count == 0
        || (_nodeMaps.TryGetValue(questId, out var maps) && maps.Any(map => MapFilter.Contains(map.Id)));

    /// <summary>
    /// Ranks raidable maps by how many quests both profiles still need a raid for
    /// there. Independent of <see cref="MapFilter"/> — this breakdown is what
    /// the filter is chosen from, so it always spans every applicable quest.
    /// </summary>
    private void BuildSharedMapRankings()
    {
        var facts = new List<SharedMapQuestFact>();
        foreach (var node in _catchUpNodes)
        {
            var primaryNeedsRaid = NeedsRaid(GetQuestState(node.Id));
            var comparisonNeedsRaid = NeedsRaid(GetComparisonQuestState(node.Id));
            foreach (var map in _nodeMaps[node.Id])
            {
                if (!IsRaidableMap(map.Id)) continue;
                facts.Add(new SharedMapQuestFact(node.Id, map.Id, map.Name, primaryNeedsRaid, comparisonNeedsRaid));
            }
        }

        var banners = _nodeMaps.Values
            .SelectMany(maps => maps)
            .GroupBy(map => map.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().BannerImageUrl, StringComparer.Ordinal);

        SharedMapRankings = SharedMapRules.BuildRanking(facts)
            .Select(ranking => new SharedMapRankingDto(
                ranking.MapId,
                ranking.MapName,
                banners.GetValueOrDefault(ranking.MapId),
                ranking.Count,
                ranking.QuestIds.Select(id => _nodeById.GetValueOrDefault(id)?.Name ?? id).ToArray()))
            .ToArray();
    }

    private void BuildCatchUpChains()
    {
        if (!CompareMode || Topology is null) { CatchUpChains = []; return; }

        var facts = _catchUpNodes
            .Where(MatchesMapFilter)
            .Select(node => new CatchUpQuestFact(
                node.Id,
                node.TraderId,
                _catchUpDepths.GetValueOrDefault(node.Id),
                GetQuestState(node.Id)?.DisplayState == "Completed",
                GetComparisonQuestState(node.Id)?.DisplayState == "Completed"))
            .ToArray();

        CatchUpChains = CatchUpRules.BuildChainGaps(facts)
            .Select(gap =>
            {
                var trader = _catchUpTraders.GetValueOrDefault(gap.TraderId);
                var quests = gap.NextQuests.Select(recommendation =>
                {
                    var node = _nodeById.GetValueOrDefault(recommendation.QuestId);
                    var state = GetQuestState(recommendation.QuestId);
                    var displayState = state?.DisplayState ?? "Locked";
                    return new CatchUpQuestDto(
                        recommendation.QuestId,
                        node?.Name ?? recommendation.QuestId,
                        recommendation.Depth,
                        displayState,
                        state?.Blockers ?? [])
                    {
                        Actionable = IsActionableState(displayState),
                        LocationName = _nodeMaps.TryGetValue(recommendation.QuestId, out var maps) && maps.Length > 0
                            ? string.Join(", ", maps.Take(2).Select(map => map.Name))
                            : string.Empty,
                    };
                }).ToArray();

                var actionable = quests.Where(quest => quest.Actionable).ToArray();
                var gated = quests.Where(quest => !quest.Actionable).ToArray();
                return new CatchUpChainDto(gap.TraderId, trader?.Name ?? gap.TraderId, trader?.ImageUrl, gap.PrimaryCompletedCount, gap.ComparisonCompletedCount, gap.Gap, actionable)
                {
                    GatedQuests = gated,
                    // The earliest missing quest is how far back the chain picks up again.
                    BehindDepth = quests.Length == 0 ? int.MaxValue : quests.Min(quest => quest.Depth),
                };
            })
            // Chains the player can act on come first, then the ones where they restart
            // furthest back, then by how many quests they are behind.
            .OrderByDescending(chain => chain.Actionable)
            .ThenBy(chain => chain.BehindDepth)
            .ThenByDescending(chain => chain.Gap)
            .ThenBy(chain => chain.TraderId, StringComparer.Ordinal)
            .ToArray();
    }

    private void UpdateComparisonCounts(IEnumerable<string> ids)
    {
        var comparisons = ids.Select(GetComparison).ToArray();
        ComparableQuestCount = comparisons.Length;
        DifferenceCount = comparisons.Count(comparison => comparison.HasDifferences);
        ComparisonCounts = Enum.GetValues<QuestDifferenceCategory>()
            .ToDictionary(category => category, category => comparisons.Count(comparison => comparison.Categories.Contains(category)));
    }

    private bool MatchesComparisonFilter(string id)
    {
        if (ComparisonFilter == QuestComparisonFilter.AllQuests) return true;
        var comparison = GetComparison(id);
        if (ComparisonFilter == QuestComparisonFilter.AllChanges) return comparison.HasDifferences;
        var category = ComparisonFilter switch
        {
            QuestComparisonFilter.PrimaryOnly => QuestDifferenceCategory.PrimaryOnly,
            QuestComparisonFilter.ComparisonOnly => QuestDifferenceCategory.ComparisonOnly,
            QuestComparisonFilter.Exclusion => QuestDifferenceCategory.Exclusion,
            QuestComparisonFilter.Status => QuestDifferenceCategory.Status,
            QuestComparisonFilter.Objectives => QuestDifferenceCategory.Objectives,
            QuestComparisonFilter.AvailableAfter => QuestDifferenceCategory.AvailableAfter,
            _ => throw new InvalidOperationException($"Unsupported comparison filter {ComparisonFilter}."),
        };
        return comparison.Categories.Contains(category);
    }

    private static bool ObjectivesMatch(QuestStateDto? primary, QuestStateDto? comparison)
    {
        var primaryObjectives = (primary?.Objectives ?? []).ToDictionary(item => item.ObjectiveId, StringComparer.Ordinal);
        var comparisonObjectives = (comparison?.Objectives ?? []).ToDictionary(item => item.ObjectiveId, StringComparer.Ordinal);
        if (!primaryObjectives.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(comparisonObjectives.Keys)) return false;
        return primaryObjectives.All(pair => ObjectiveMatches(pair.Value, comparisonObjectives[pair.Key]));
    }

    private static bool ObjectiveMatches(ObjectiveProgressDto? primary, ObjectiveProgressDto? comparison) =>
        primary is null && comparison is null
        || primary is not null && comparison is not null
        && primary.Complete == comparison.Complete
        && primary.Current == comparison.Current
        && primary.Required == comparison.Required
        && primary.ProgressKnown == comparison.ProgressKnown;

    private string ViewportScope() => Topology is null || Profile is null
        ? string.Empty
        : string.Join('|', Topology.Version, Profile.ProfileId, ComparisonProfile?.ProfileId ?? string.Empty, ShowAllFuture ? 1 : 0, ShowFinished ? 1 : 0, ShowRepeatables && !CompareMode ? 1 : 0, LevelEligibleOnly ? 1 : 0, CompareMode ? ComparisonFilter : QuestComparisonFilter.AllQuests, FilterKey(TraderFilter), FilterKey(MapFilter), Search.Trim(), FocusedId ?? string.Empty, SelectedId ?? string.Empty);

    private void CaptureCurrentProfileSettings()
    {
        if (Profile is null) return;
        var settings = new QuestProfileUiSettings(SelectedId, FocusedId);
        if (ComparisonProfile is null) _profileSettings[Profile.ProfileId] = settings;
        else _comparePairSettings[ComparePairKey(Profile.ProfileId, ComparisonProfile.ProfileId)] = settings;
    }

    private void RestoreCurrentProfileSettings()
    {
        QuestProfileUiSettings? saved = null;
        if (Profile is not null)
        {
            var source = ComparisonProfile is null ? _profileSettings : _comparePairSettings;
            var key = ComparisonProfile is null ? Profile.ProfileId : ComparePairKey(Profile.ProfileId, ComparisonProfile.ProfileId);
            source.TryGetValue(key, out saved);
        }
        SelectedId = saved?.SelectedId is { } selected && _applicable.Contains(selected) ? selected : null;
        FocusedId = saved?.FocusedId is { } focused && _applicable.Contains(focused) && !_repeatableIds.Contains(focused) ? focused : null;
        FocusIds = FocusedId is null ? null : ComputeChain(FocusedId);
    }

    private static string ComparePairKey(string primaryId, string comparisonId) => $"{primaryId}|{comparisonId}";

    private static Dictionary<string, List<IndexedQuestEdge>> GroupEdges(IReadOnlyList<QuestEdgeDto> edges, Func<QuestEdgeDto, string> keySelector)
    {
        var result = new Dictionary<string, List<IndexedQuestEdge>>(StringComparer.Ordinal);
        for (var index = 0; index < edges.Count; index++)
        {
            var edge = edges[index];
            var key = keySelector(edge);
            if (!result.TryGetValue(key, out var values)) result[key] = values = [];
            values.Add(new IndexedQuestEdge(index, edge));
        }

        return result;
    }
}

public sealed record IndexedQuestEdge(int Index, QuestEdgeDto Edge);
public sealed record InProgressQuest(QuestNodeDto Node, QuestStateDto? PrimaryState, QuestStateDto? ComparisonState)
{
    public QuestStateDto State => PrimaryState ?? ComparisonState ?? throw new InvalidOperationException("An in-progress quest requires at least one profile state.");
}
public sealed record InProgressTraderGroup(QuestTraderDto Trader, IReadOnlyList<InProgressQuest> Quests);
public sealed record QuestProfileUiSettings(string? SelectedId, string? FocusedId);
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QuestDifferenceCategory { PrimaryOnly, ComparisonOnly, Exclusion, Status, Objectives, AvailableAfter }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QuestComparisonFilter { AllQuests, AllChanges, PrimaryOnly, ComparisonOnly, Exclusion, Status, Objectives, AvailableAfter }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QuestDetailTextTab { Description, Summary }
public sealed record QuestComparisonDto(string QuestId, QuestDifferenceCategory[] Categories)
{
    public bool HasDifferences => Categories.Length > 0;
    public static QuestComparisonDto Match(string questId) => new(questId, []);
}
public sealed record QuestMapSettings(string? SelectedProfileId, string? Language, bool ShowAllFuture, bool ShowFinished, bool LevelEligibleOnly, bool InProgressExpanded, string? Search, string? TraderFilter, IReadOnlyDictionary<string, QuestProfileUiSettings>? Profiles)
{
    public bool ShowRepeatables { get; init; } = true;
    public bool CompareEnabled { get; init; }
    public string? ComparisonProfileId { get; init; }
    public QuestComparisonFilter ComparisonFilter { get; init; }
    public QuestDetailTextTab DetailTextTab { get; init; }
    public bool CatchUpExpanded { get; init; }
    public string? MapFilter { get; init; } = string.Empty;
    public IReadOnlyList<string>? TraderFilters { get; init; }
    public IReadOnlyList<string>? MapFilters { get; init; }
    public int? LevelCap { get; init; }
    public IReadOnlyDictionary<string, QuestProfileUiSettings>? ComparePairs { get; init; } = new Dictionary<string, QuestProfileUiSettings>();
    public static QuestMapSettings Default { get; } = new(null, null, false, true, true, false, string.Empty, string.Empty, new Dictionary<string, QuestProfileUiSettings>());
}
public sealed record QuestGraphView(string? ProfileId, string? ComparisonProfileId, bool CompareMode, string Language, string BrowserLocale, string[] VisibleQuestIds, string? SelectedId, string? FocusedId, string[] PrerequisiteIds, string[] SuccessorIds, int[] HighlightedEdgeIndexes, QuestComparisonDto[] Comparisons, bool InProgressExpanded, string ViewportScope, string PrimaryLabel, string ComparisonLabel, IReadOnlyDictionary<string, string> Strings);
public sealed record QuestGraphSnapshot(QuestTopologyDto Topology, ProfileStateDto? Profile, ProfileStateDto? ComparisonProfile, QuestGraphView View);
/// <summary>Quests still needing a raid on one location, per profile and shared.</summary>
public sealed record MapQuestCounts(int Primary, int Comparison, int Shared);

/// <summary>How far one pooled profile is from quests only another of them can currently do.</summary>
/// <summary>
/// How far one pooled profile is from quests only another of them can currently do, plus the
/// names of the quests standing in the way, closest first.
/// </summary>
public sealed record CoopBehindDto(string ProfileId, string Label, int Furthest, int Count, IReadOnlyList<string> Blocking);

/// <summary>A map worth running to close one player's gap, and what they are behind on there.</summary>
public sealed record CatchUpMapEntryDto(string MapId, string MapName, int Count, IReadOnlyList<string> QuestNames);

/// <summary>One player's catch-up map ranking.</summary>
public sealed record PlayerCatchUpMapsDto(string ProfileId, string Label, IReadOnlyList<CatchUpMapEntryDto> Maps);

/// <summary>One kind of thing a quest asks for, and how much of it.</summary>
public sealed record ObjectiveMixEntry(string Kind, int Amount);

public sealed record QuestGraphUpdate(string[] VisibleQuestIds, string? SelectedId, string? FocusedId, string[] PrerequisiteIds, string[] SuccessorIds, int[] HighlightedEdgeIndexes, QuestComparisonDto[] Comparisons, bool CompareMode, bool InProgressExpanded, string ViewportScope, string PrimaryLabel, string ComparisonLabel);
