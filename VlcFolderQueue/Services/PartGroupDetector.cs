using System.IO;
using System.Text.RegularExpressions;
using VlcFolderQueue.Data;

namespace VlcFolderQueue.Services;

/// <summary>
/// Finds multi-part episodes ("... (Part 1).mp4" + "... (Part 2)-Subtitle.mp4") within a
/// folder's files so they can be bonded into one queue unit instead of being picked
/// independently — a "Part 2" playing without its "Part 1" (or vice versa, or out of order)
/// defeats the point of a two-part story.
/// </summary>
public static partial class PartGroupDetector
{
    [GeneratedRegex(@"\bp(?:ar)?t\.?\s*(\d{1,2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex PartPattern();

    public static int? GetPartNumber(string fileName)
    {
        var match = PartPattern().Match(fileName);
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    /// <summary>
    /// Groups files whose part numbers form an ascending run starting at 1 (Part 1, Part 2, ...)
    /// and are adjacent to each other in natural filename order. A lone "Part 1" with no
    /// following "Part 2" (or vice versa) isn't returned as a group — it stays standalone.
    /// </summary>
    public static List<List<FileEntry>> FindGroups(IEnumerable<FileEntry> filesInFolder)
    {
        var sorted = filesInFolder.OrderBy(f => f.Path, NaturalPathComparer.Instance).ToList();
        var groups = new List<List<FileEntry>>();

        var i = 0;
        while (i < sorted.Count)
        {
            var partNum = GetPartNumber(Path.GetFileName(sorted[i].Path));
            if (partNum != 1) { i++; continue; }

            var group = new List<FileEntry> { sorted[i] };
            var expected = 2;
            var j = i + 1;
            while (j < sorted.Count && GetPartNumber(Path.GetFileName(sorted[j].Path)) == expected)
            {
                group.Add(sorted[j]);
                expected++;
                j++;
            }

            if (group.Count >= 2)
            {
                groups.Add(group);
                i = j; // skip past the whole group
            }
            else
            {
                i++;
            }
        }

        return groups;
    }
}
