namespace SPTQuestMap.Core.Rules;

/// <summary>
/// Computes each quest's position in its prerequisite chain as the length of the
/// longest path of edges leading to it (0 for quests with no prerequisites).
/// Shared by the "catch up" planner to compare how far two profiles have
/// progressed through the same questline.
/// </summary>
public static class QuestChainDepthCalculator
{
    /// <summary>
    /// Computes longest-path depth per quest id over the given DAG. Edges pointing
    /// at unknown quest ids, and edges that would form a cycle, are ignored so a
    /// malformed topology can never hang this in an infinite loop.
    /// </summary>
    public static IReadOnlyDictionary<string, int> ComputeDepths(
        IReadOnlyCollection<string> questIds,
        IReadOnlyCollection<(string SourceId, string TargetId)> edges)
    {
        var ids = questIds.ToHashSet(StringComparer.Ordinal);
        var outgoing = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var incomingCount = ids.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!ids.Contains(edge.SourceId) || !ids.Contains(edge.TargetId) || edge.SourceId == edge.TargetId) continue;
            if (!outgoing.TryGetValue(edge.SourceId, out var targets)) outgoing[edge.SourceId] = targets = [];
            targets.Add(edge.TargetId);
            incomingCount[edge.TargetId]++;
        }

        // Kahn's algorithm over the DAG, tracking the longest path seen so far into each
        // node. Anything left stranded on a cycle never reaches zero remaining in-edges,
        // so it is never enqueued and simply keeps its initial depth of 0.
        var depth = ids.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var remaining = new Dictionary<string, int>(incomingCount, StringComparer.Ordinal);
        var queue = new Queue<string>(ids.Where(id => incomingCount[id] == 0).OrderBy(id => id, StringComparer.Ordinal));
        while (queue.TryDequeue(out var current))
        {
            if (!outgoing.TryGetValue(current, out var targets)) continue;
            foreach (var target in targets)
            {
                depth[target] = Math.Max(depth[target], depth[current] + 1);
                if (--remaining[target] == 0) queue.Enqueue(target);
            }
        }

        return depth;
    }
}
