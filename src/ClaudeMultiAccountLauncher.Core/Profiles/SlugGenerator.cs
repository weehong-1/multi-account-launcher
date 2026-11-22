using System.Globalization;
using System.Text;

namespace ClaudeMultiAccountLauncher.Core.Profiles;

/// <summary>Derives a kebab-case slug from a display name, unique among existing slugs (FR-2).</summary>
public static class SlugGenerator
{
    public const int MaxLength = 40;
    private const string Fallback = "profile";

    /// <summary>
    /// Slugs that would be ambiguous in <c>--launch &lt;slug&gt;</c>: the built-in Claude Default
    /// and WhatsApp Default. One slug space is shared by Claude and WhatsApp profiles.
    /// </summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        Startup.StartupCommand.DefaultSlug,
        Startup.StartupCommand.WhatsAppDefaultSlug,
    };

    public static string Create(string name, IEnumerable<string> existingSlugs)
    {
        var taken = new HashSet<string>(existingSlugs, StringComparer.OrdinalIgnoreCase);
        taken.UnionWith(Reserved);

        var baseSlug = Kebab(name);
        if (!taken.Contains(baseSlug))
        {
            return baseSlug;
        }

        for (var n = 2; ; n++)
        {
            var suffix = "-" + n.ToString(CultureInfo.InvariantCulture);
            var candidate = baseSlug[..Math.Min(baseSlug.Length, MaxLength - suffix.Length)].TrimEnd('-') + suffix;
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    internal static string Kebab(string name)
    {
        // Strip accents: "Café" -> "Cafe"
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var pendingDash = false;

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(c);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingDash && sb.Length > 0)
                {
                    sb.Append('-');
                }

                sb.Append(lower);
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }
        }

        var slug = sb.Length > MaxLength ? sb.ToString(0, MaxLength).TrimEnd('-') : sb.ToString();
        return slug.Length == 0 ? Fallback : slug;
    }
}
