using NUnit.Framework;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Core.Tests;

[TestFixture]
public sealed class QuestSeriesRulesTests
{
    // The separator and the word for "part" both change between installed locales,
    // so the parser matches the shape rather than any particular language.
    [TestCase("Gunsmith - Part 5", "Gunsmith", 5)]
    [TestCase("Büchsenmacher - Teil 12", "Büchsenmacher", 12)]
    [TestCase("Armurier - Partie 3", "Armurier", 3)]
    [TestCase("Оружейник. Часть 1", "Оружейник", 1)]
    [TestCase("Cargo X - 4", "Cargo X", 4)]
    [TestCase("Health Care Privacy - Part 6", "Health Care Privacy", 6)]
    [TestCase("The Survivalist Path - Zhivchik", null, 0)]
    [TestCase("Big Sale", null, 0)]
    [TestCase("Debut", null, 0)]
    [TestCase("", null, 0)]
    [TestCase(null, null, 0)]
    public void TryParse_SplitsNumberedSeriesAndLeavesStandaloneQuestsAlone(string? questName, string? expectedSeries, int expectedPart)
    {
        var parsed = QuestSeriesRules.TryParse(questName, out var series, out var part);

        if (expectedSeries is null)
        {
            Assert.That(parsed, Is.False, $"'{questName}' should not be treated as a series");
            return;
        }

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(series, Is.EqualTo(expectedSeries));
            Assert.That(part, Is.EqualTo(expectedPart));
        });
    }

    [Test]
    public void BuildProgress_ReportsHighestCompletedPartForEachProfile()
    {
        var facts = new[]
        {
            Fact("Gunsmith", 1, primary: true, comparison: true),
            Fact("Gunsmith", 2, primary: true, comparison: true),
            Fact("Gunsmith", 3, primary: false, comparison: true),
            Fact("Gunsmith", 4, primary: false, comparison: false),
        };

        var progress = QuestSeriesRules.BuildProgress(facts).Single();

        Assert.Multiple(() =>
        {
            Assert.That(progress.SeriesName, Is.EqualTo("Gunsmith"));
            Assert.That(progress.TotalParts, Is.EqualTo(4));
            Assert.That(progress.PrimaryFurthest, Is.EqualTo(2));
            Assert.That(progress.ComparisonFurthest, Is.EqualTo(3));
        });
    }

    [Test]
    public void BuildProgress_UsesFurthestPartNotACountOfCompletedParts()
    {
        // A player who skipped ahead is at the part they reached, not at "two done".
        var facts = new[]
        {
            Fact("Spa Tour", 1, primary: true, comparison: false),
            Fact("Spa Tour", 2, primary: false, comparison: false),
            Fact("Spa Tour", 5, primary: true, comparison: false),
        };

        var progress = QuestSeriesRules.BuildProgress(facts).Single();

        Assert.That(progress.PrimaryFurthest, Is.EqualTo(5));
    }

    [Test]
    public void BuildProgress_OmitsSeriesNeitherProfileHasStarted()
    {
        var facts = new[]
        {
            Fact("Started", 1, primary: true, comparison: false),
            Fact("Untouched", 1, primary: false, comparison: false),
            Fact("Untouched", 2, primary: false, comparison: false),
        };

        Assert.That(QuestSeriesRules.BuildProgress(facts).Select(entry => entry.SeriesName), Is.EqualTo(new[] { "Started" }));
    }

    [Test]
    public void BuildProgress_RanksTheBiggestDisagreementFirst()
    {
        var facts = new[]
        {
            // level: both at 2
            Fact("Level", 1, primary: true, comparison: true),
            Fact("Level", 2, primary: true, comparison: true),
            // wide: primary 1, comparison 4
            Fact("Wide", 1, primary: true, comparison: true),
            Fact("Wide", 4, primary: false, comparison: true),
            // narrow: primary 2, comparison 1
            Fact("Narrow", 1, primary: true, comparison: true),
            Fact("Narrow", 2, primary: true, comparison: false),
        };

        Assert.That(
            QuestSeriesRules.BuildProgress(facts).Select(entry => entry.SeriesName),
            Is.EqualTo(new[] { "Wide", "Narrow", "Level" }));
    }

    private static QuestSeriesFact Fact(string series, int part, bool primary, bool comparison) =>
        new($"{series}-{part}", series, part, primary, comparison);
}
