using NUnit.Framework;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Core.Tests
{
    [TestFixture]
    public sealed class ProfileLabelRulesTests
    {
        [TestCase("Woulf", "fallback", "Wo")]
        [TestCase("ScramJet", "fallback", "Sc")]
        public void ShortLabel_ReturnsFirstTwoCharacters(string nickname, string fallback, string expected)
        {
            // Arrange and Act
            var result = ProfileLabelRules.ShortLabel(nickname, fallback);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase(null, "fallback", "fallback")]
        [TestCase("", "fallback", "fallback")]
        [TestCase("   ", "fallback", "fallback")]
        public void ShortLabel_ReturnsFallbackForNullEmptyOrWhitespace(string? nickname, string fallback, string expected)
        {
            // Arrange and Act
            var result = ProfileLabelRules.ShortLabel(nickname, fallback);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase("  Woulf  ", "fallback", "Wo")]
        public void ShortLabel_TrimmingLeadingTrailingWhitespace(string nickname, string fallback, string expected)
        {
            // Arrange and Act
            var result = ProfileLabelRules.ShortLabel(nickname, fallback);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase("a", "fallback", "a")]
        public void ShortLabel_ReturnsSingleCharacter(string nickname, string fallback, string expected)
        {
            // Arrange and Act
            var result = ProfileLabelRules.ShortLabel(nickname, fallback);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        // This test exists to ensure that the method handles surrogate pairs correctly, 
        // i.e., it doesn't cut an emoji in half.
        [TestCase("😀😀x", "fallback", "😀😀")]
        public void ShortLabel_ReturnsWholeEmojiCharacters(string nickname, string fallback, string expected)
        {
            // Arrange and Act
            var result = ProfileLabelRules.ShortLabel(nickname, fallback);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
            Assert.That(result, Does.Not.Contain("\uFFFD"));
            Assert.That(new System.Globalization.StringInfo(result).LengthInTextElements, Is.EqualTo(2));
        }

        [TestCase("ab", "fallback", "ab")]
        public void ShortLabel_ReturnsTwoCharacters(string nickname, string fallback, string expected)
        {
            // Arrange and Act
            var result = ProfileLabelRules.ShortLabel(nickname, fallback);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }
    }
}
