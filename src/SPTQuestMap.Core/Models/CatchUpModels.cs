namespace SPTQuestMap.Core.Models;

/// <summary>Minimal per-quest facts the catch-up planner needs, decoupled from any DTO layer.</summary>
public sealed record CatchUpQuestFact(string QuestId, string TraderId, int Depth, bool PrimaryCompleted, bool ComparisonCompleted);

/// <summary>One recommended next quest within a chain, ordered nearest-first.</summary>
public sealed record CatchUpQuestRecommendation(string QuestId, int Depth);

/// <summary>A single trader questline ranked by how far the comparison profile is ahead.</summary>
public sealed record CatchUpChainGap(string TraderId, int PrimaryDepth, int ComparisonDepth, IReadOnlyList<CatchUpQuestRecommendation> NextQuests)
{
    public int Gap => ComparisonDepth - PrimaryDepth;
}
