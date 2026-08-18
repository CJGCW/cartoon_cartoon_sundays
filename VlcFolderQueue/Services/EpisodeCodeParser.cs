using System.Text.RegularExpressions;

namespace VlcFolderQueue.Services;

/// <summary>Pulls a season/episode code (S01E01, 01x01, Season 1 Episode 01, ...) out of a filename.</summary>
public static partial class EpisodeCodeParser
{
    [GeneratedRegex(@"S\d{1,4}\s?E\d{1,3}(?:-?E?\d{1,3})?", RegexOptions.IgnoreCase)]
    private static partial Regex SxxExxPattern();

    [GeneratedRegex(@"(?<!\d)\d{1,3}x\d{2,3}(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex NxNPattern();

    [GeneratedRegex(@"Season\s*\d{1,3}\s*Episode\s*\d{1,3}", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonEpisodePattern();

    public static string? TryExtract(string fileName)
    {
        var match = SxxExxPattern().Match(fileName);
        if (match.Success) return match.Value;

        match = SeasonEpisodePattern().Match(fileName);
        if (match.Success) return match.Value;

        match = NxNPattern().Match(fileName);
        if (match.Success) return match.Value;

        return null;
    }
}
