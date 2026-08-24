namespace SPTQuestMap.Core.Models;

/// <summary>Minimal per-quest facts the catch-up planner needs, decoupled from any DTO layer.</summary>
public sealed record CatchUpQuestFact(string QuestId, string TraderId, int Depth, bool PrimaryCompleted, bool ComparisonCompleted);

/// <summary>One recommended next quest within a chain, ordered nearest-first.</summary>
public sealed record CatchUpQuestRecommendation(string QuestId, int Depth);

/// <summary>
/// A single trader questline ranked by how many quests the comparison profile has
/// completed that the primary profile has not. Gap is a count of specific missing
/// quests, not a depth-scalar difference — two profiles can tie on "deepest completed
/// quest" for a trader while still being behind each other on different branches of
/// that trader's tree, so depth alone can't detect a real gap.
/// </summary>
public sealed record CatchUpChainGap(string TraderId, int PrimaryCompletedCount, int ComparisonCompletedCount, int Gap, IReadOnlyList<CatchUpQuestRecommendation> NextQuests);

/// <summary>
/// One (quest, map) pairing fact for the shared-map ranking. A quest with objectives on
/// multiple maps appears once per map it can be worked on. "Actionable" means the quest
/// is currently available to play right now (available, in progress, or ready to turn
/// in) — not completed and not blocked behind something else.
/// </summary>
public sealed record SharedMapQuestFact(string QuestId, string MapId, string MapName, bool PrimaryActionable, bool ComparisonActionable);

/// <summary>
/// One map ranked by how many quests both profiles can currently work on there
/// together, largest shared count first.
/// </summary>
public sealed record SharedMapRanking(string MapId, string MapName, int Count, IReadOnlyList<string> QuestIds);

/// <summary>One numbered entry in a named quest series, with each profile's completion of it.</summary>
public sealed record QuestSeriesFact(string QuestId, string SeriesName, int Part, bool PrimaryCompleted, bool ComparisonCompleted);

/// <summary>
/// How far each profile has advanced through one named series. Positions are the
/// highest completed part number; 0 means the series has not been started.
/// </summary>
public sealed record QuestSeriesProgress(string SeriesName, int TotalParts, int PrimaryFurthest, int ComparisonFurthest);
