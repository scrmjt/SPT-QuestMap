using System.Text.Json;
using NUnit.Framework;

namespace SPTQuestMap.Core.Tests;

/// <summary>
/// Guards the BepInEx client's embedded locale catalogs.
///
/// These are hand-edited JSON files that nothing else validates, and
/// <c>ClientLocale.LoadCatalog</c> calls <c>JsonConvert.DeserializeObject</c> without a
/// try/catch. A malformed file therefore throws on the very first <c>ClientLocale.Text</c>
/// call, which in the Tasks screen means the mod catches, disposes and silently reverts to the
/// vanilla panel - for every player using that language, with nothing in the log pointing at
/// the locale file.
///
/// This is not hypothetical: a missing comma in <c>ru.json</c> shipped in the committed tree
/// and broke the entire Russian catalog. Nobody noticed, because the client cannot be run
/// here and English was unaffected.
/// </summary>
[TestFixture]
public sealed class ClientLocaleCatalogTests
{
    // Newtonsoft tolerates the trailing commas these files use; System.Text.Json needs telling.
    private static readonly JsonDocumentOptions ParseOptions = new() { AllowTrailingCommas = true };

    private static string LocalesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, "src", "SPTQuestMap.Client", "Localization", "Locales");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find src/SPTQuestMap.Client/Localization/Locales above " + AppContext.BaseDirectory);
    }

    private static string[] LocaleFiles() => Directory.GetFiles(LocalesDirectory(), "*.json");

    [Test]
    public void EveryClientLocaleFileParses()
    {
        var files = LocaleFiles();
        Assert.That(files, Is.Not.Empty, "No client locale files were found.");

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotThrow(
                () => JsonDocument.Parse(text, ParseOptions).Dispose(),
                $"{Path.GetFileName(file)} is not valid JSON, so its whole catalog fails to load in game.");
        }
    }

    /// <summary>
    /// A duplicate key is valid JSON but silently discards one of the two values, so the string
    /// that actually renders depends on file order rather than on what anyone intended.
    /// </summary>
    [Test]
    public void NoClientLocaleFileRepeatsAKey()
    {
        foreach (var file in LocaleFiles())
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file), ParseOptions);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = document.RootElement
                .EnumerateObject()
                .Where(property => !seen.Add(property.Name))
                .Select(property => property.Name)
                .ToArray();

            Assert.That(duplicates, Is.Empty,
                $"{Path.GetFileName(file)} defines the same key more than once: {string.Join(", ", duplicates)}");
        }
    }

    /// <summary>
    /// Every catalog is a flat string-to-string map; <c>ClientLocale</c> deserializes it as
    /// <c>Dictionary&lt;string, string&gt;</c>, so a nested object or a number would throw the
    /// same way a syntax error does.
    /// </summary>
    [Test]
    public void EveryClientLocaleValueIsAString()
    {
        foreach (var file in LocaleFiles())
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file), ParseOptions);
            var offenders = document.RootElement
                .EnumerateObject()
                .Where(property => property.Value.ValueKind != JsonValueKind.String)
                .Select(property => $"{property.Name} ({property.Value.ValueKind})")
                .ToArray();

            Assert.That(offenders, Is.Empty,
                $"{Path.GetFileName(file)} has non-string values: {string.Join(", ", offenders)}");
        }
    }
}
