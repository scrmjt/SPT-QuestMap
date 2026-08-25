namespace SPTQuestMap.Core.Rules;

/// <summary>
/// Provides rules for generating short labels from profile nicknames, handling Unicode text elements correctly.
/// This is crucial because some profile names may contain characters outside the Basic Multilingual Plane, 
/// where a single visible character is represented by a surrogate pair.
/// </summary>
public static class ProfileLabelRules
{
    /// <summary>
    /// Generates a short label from a profile nickname, ensuring that Unicode text elements are handled as a whole unit.
    /// This prevents broken replacement glyphs from being produced when a surrogate pair is split.
    /// </summary>
    /// <param name="nickname">The profile nickname to generate a short label from.</param>
    /// <param name="fallback">The fallback label to use if the nickname is null, empty, or whitespace only.</param>
    /// <returns>A short label for the profile.</returns>
    public static string ShortLabel(string? nickname, string fallback)
    {
        var trimmedNickname = nickname?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedNickname))
        {
            return fallback;
        }

        var textElements = System.Globalization.StringInfo.GetTextElementEnumerator(trimmedNickname);
        var label = new System.Text.StringBuilder();
        var count = 0;

        while (textElements.MoveNext() && count < 2)
        {
            label.Append(textElements.GetTextElement());
            count++;
        }

        return label.ToString();
    }
}
