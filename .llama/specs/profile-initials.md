Write the complete C# file `src/SPTQuestMap.Core/Rules/ProfileLabelRules.cs`.

Namespace: SPTQuestMap.Core.Rules
Target: netstandard2.1. Nullable reference types are ENABLED. Do not use C# features
newer than what netstandard2.1 with a modern compiler allows; avoid file-scoped
namespaces is NOT required - file-scoped namespace declarations are fine and preferred.

Public static class `ProfileLabelRules` with one public method:

    public static string ShortLabel(string? nickname, string fallback)

Purpose: Compare Mode currently labels the two profiles "A" and "B". Instead each
profile should be labelled with the first two characters of its nickname, so "Woulf"
becomes "Wo" and "ScramJet" becomes "Sc".

Behaviour, exactly:
- Trim leading and trailing whitespace from nickname first.
- If nickname is null, empty, or whitespace only, return `fallback` unchanged.
- If the trimmed nickname has one character, return that single character.
- Otherwise return the first two characters of the trimmed nickname.
- Never throw for any input.

IMPORTANT correctness requirement: some profile names begin with characters outside the
Basic Multilingual Plane (for example an emoji), where a single visible character is
represented by two C# chars (a surrogate pair). Naively taking `Substring(0, 2)` would
split such a pair and produce a broken replacement glyph. Use
`System.Globalization.StringInfo` (or equivalent text-element enumeration) so that
"first two characters" means the first two USER-VISIBLE text elements, never half of a
surrogate pair.

Add a concise XML doc comment on the class and on the method explaining WHY the
text-element handling matters. Do not restate what the code plainly does.

Return only the code.
