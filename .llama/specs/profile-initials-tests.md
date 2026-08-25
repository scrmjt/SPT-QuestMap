Write the complete NUnit test file `tests/SPTQuestMap.Core.Tests/ProfileLabelRulesTests.cs`.

Namespace: SPTQuestMap.Core.Tests
Uses: NUnit.Framework, SPTQuestMap.Core.Rules
Target framework of the test project is net9.0. Nullable is enabled.

It tests this existing production method:

    namespace SPTQuestMap.Core.Rules;
    public static class ProfileLabelRules
    {
        public static string ShortLabel(string? nickname, string fallback);
    }

Specified behaviour of ShortLabel:
- Trims leading/trailing whitespace from nickname first.
- null, empty, or whitespace-only nickname returns `fallback` unchanged.
- A trimmed nickname of one character returns that single character.
- Otherwise returns the first two USER-VISIBLE text elements of the trimmed nickname.
- Never throws.
- Crucially: a nickname starting with a character outside the Basic Multilingual Plane
  (an emoji, which is a surrogate pair in C#) must not be cut in half. Taking the first
  two text elements of "😀😀x" must yield the two whole emoji, not two broken halves.

Write a `[TestFixture] public sealed class ProfileLabelRulesTests` covering:
- "Woulf" -> "Wo" and "ScramJet" -> "Sc" (use [TestCase] for these ordinary cases)
- null, "", and "   " all return the supplied fallback
- leading/trailing whitespace is trimmed before taking characters, e.g. "  Woulf  " -> "Wo"
- a single character nickname returns that one character
- an emoji-leading nickname returns two whole emoji. Assert THREE things: the result
  equals the expected string literal; the result contains no U+FFFD replacement
  character; and `new System.Globalization.StringInfo(result).LengthInTextElements`
  equals 2. Do NOT assert anything about char.IsLowSurrogate of the final char - for a
  correctly handled emoji pair the final char legitimately IS a low surrogate, so such an
  assertion would fail against correct code.
- a nickname that is exactly two characters returns itself

Give each test a name that states the behaviour it pins. Add a brief comment above the
surrogate-pair test explaining why it exists. Do not test anything the specification
above does not state.

Return only the code.
