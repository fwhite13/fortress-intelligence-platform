using System.Text.RegularExpressions;

namespace FortressNexus.Web.Services;

public static partial class SlugHelper
{
    public static string Slugify(string input)
    {
        var lower = input.ToLowerInvariant();
        var spaced = lower.Replace(' ', '-');
        return AlphanumHyphenRegex().Replace(spaced, "");
    }

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex AlphanumHyphenRegex();
}
