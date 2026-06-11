using System.Text;

namespace AppShelf.Core.Models;

/// <summary>
/// Derives a stable, lowercase, URL/id-safe slug from a display name (spec §2: id is a
/// slug derived from name if not given).
/// </summary>
public static class Slug
{
    /// <summary>
    /// Lowercases, replaces any run of non-alphanumeric characters with a single hyphen,
    /// and trims leading/trailing hyphens. Returns "app" if the input has no usable
    /// characters, so a slug is never empty.
    /// </summary>
    public static string From(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "app";

        var sb = new StringBuilder(name.Length);
        bool lastWasHyphen = false;

        foreach (char c in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen && sb.Length > 0)
            {
                sb.Append('-');
                lastWasHyphen = true;
            }
        }

        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "app" : slug;
    }
}
