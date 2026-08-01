using System.Globalization;
using System.Text;

namespace NeuronCodec.Web.Services;

public static class Slug
{
    /// <summary>
    /// Lowercase, ASCII, hyphen-separated. Accented characters are folded to their base letter
    /// so "Décodage" becomes "decodage" rather than losing the character entirely.
    /// </summary>
    public static string From(string? input, string fallback = "untitled")
    {
        if (string.IsNullOrWhiteSpace(input)) return fallback;

        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var lastWasHyphen = false;

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch) && ch < 128)
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen && sb.Length > 0)
            {
                sb.Append('-');
                lastWasHyphen = true;
            }
        }

        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? fallback : slug;
    }

    /// <summary>
    /// Appends -2, -3, … until <paramref name="isTaken"/> stops matching. Used when a title
    /// collides with an existing slug.
    /// </summary>
    public static string Unique(string candidate, Func<string, bool> isTaken)
    {
        if (!isTaken(candidate)) return candidate;

        for (var n = 2; n < 10_000; n++)
        {
            var next = $"{candidate}-{n}";
            if (!isTaken(next)) return next;
        }

        return $"{candidate}-{Guid.NewGuid():N}";
    }
}
