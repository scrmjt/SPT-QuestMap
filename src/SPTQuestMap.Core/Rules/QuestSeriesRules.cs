using System.Text.RegularExpressions;
using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Rules;

/// <summary>
/// Groups quests into named series ("Gunsmith - Part 5") so a profile's position in a
/// questline can be shown as a step within that series.
/// </summary>
public static class QuestSeriesRules
{
    // Deliberately structural rather than English-specific: EFT keeps the
    // "<name><separator><word> <number>" shape across translations, but neither the
    // separator nor the word for "part" is stable — English and most locales use
    // "Gunsmith - Part 5", Russian uses "Оружейник. Часть 1". Matching the shape
    // rather than the word keeps every installed locale working. The trailing word is
    // optional so "Cargo X - 3" style names still group.
    private static readonly Regex SeriesPattern = new(
        @"^(?<series>.*?)\s*[-–—.]\s*(?:[^\W\d_]+\.?\s+)?(?<part>\d+)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Splits a display name into its series name and part number, or returns false
    /// when the quest is a standalone quest rather than part of a numbered series.
    /// </summary>
    public static bool TryParse(string? questName, out string seriesName, out int part)
    {
        seriesName = string.Empty;
        part = 0;
        if (string.IsNullOrWhiteSpace(questName)) return false;

        var match = SeriesPattern.Match(questName);
        if (!match.Success) return false;

        var name = match.Groups["series"].Value.Trim();
        if (name.Length == 0) return false;
        if (!int.TryParse(match.Groups["part"].Value, out part) || part <= 0) return false;

        seriesName = name;
        return true;
    }

    /// <summary>
    /// Builds one <see cref="QuestSeriesProgress"/> per series present in
    /// <paramref name="facts"/>, reporting how far each profile has advanced.
    /// Series neither profile has started are omitted.
    /// </summary>
    /// <remarks>
    /// "How far" is the highest completed part number, not a count of completed parts:
    /// the question being answered is "where is this player in the chain", and a player
    /// who completed parts 1, 2 and 5 is at part 5 regardless of the gap behind them.
    /// The denominator is the whole series, not just the parts on a filtered map, so a
    /// position stays comparable no matter which map is selected.
    /// </remarks>
    public static IReadOnlyList<QuestSeriesProgress> BuildProgress(IReadOnlyCollection<QuestSeriesFact> facts)
    {
        var progress = new List<QuestSeriesProgress>();
        foreach (var group in facts.GroupBy(fact => fact.SeriesName, StringComparer.Ordinal))
        {
            var parts = group.ToArray();
            var total = parts.Max(fact => fact.Part);
            var primary = Furthest(parts, fact => fact.PrimaryCompleted);
            var comparison = Furthest(parts, fact => fact.ComparisonCompleted);
            if (primary == 0 && comparison == 0) continue;

            progress.Add(new QuestSeriesProgress(group.Key, total, primary, comparison));
        }

        // Biggest disagreement first — that is where the two players diverge and so
        // what the comparison is being read for.
        return progress
            .OrderByDescending(entry => Math.Abs(entry.ComparisonFurthest - entry.PrimaryFurthest))
            .ThenBy(entry => entry.SeriesName, StringComparer.Ordinal)
            .ToArray();
    }

    private static int Furthest(IReadOnlyCollection<QuestSeriesFact> parts, Func<QuestSeriesFact, bool> completed)
    {
        var done = parts.Where(completed).Select(fact => fact.Part).ToArray();
        return done.Length == 0 ? 0 : done.Max();
    }
}
