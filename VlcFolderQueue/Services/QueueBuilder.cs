using VlcFolderQueue.Data;

namespace VlcFolderQueue.Services;

public class QueueItem
{
    public string FilePath { get; set; } = "";
    public double DurationSeconds { get; set; }
}

public class QueueResult
{
    public List<QueueItem> Items { get; set; } = new();
    public double TotalSeconds => Items.Sum(i => i.DurationSeconds);
    public bool MetLowerBound { get; set; }
}

public static class QueueBuilder
{
    private const double ToleranceFraction = 0.15;

    /// <summary>
    /// Builds a randomized, time-boxed queue from eligible files (unplayed, included,
    /// non-excluded, duration known). Selection is fair PER SHOW, not per file: at each draw,
    /// every show with anything left to offer has an equal chance of being picked next,
    /// regardless of how many files it has. Without this, a show with hundreds of files
    /// (each its own candidate) would dominate the draw over a show with a couple dozen,
    /// purely because it has proportionally more "tickets" in the pool.
    /// Episodic only changes WHICH unit a show offers when it's picked: instead of a random
    /// episode, it offers its earliest not-yet-played unit (natural/numeric order), so picking
    /// that show queues up "the next one," not a random episode or the whole series as a block.
    /// Multi-part episodes ("... (Part 1)" + "... (Part 2)") are always bonded into one unit,
    /// in order, regardless of whether their show is Episodic — a Part 2 playing without its
    /// Part 1 (or vice versa) defeats the point of a two-part story.
    /// Checks the resulting total *before* committing each pick so a single long file (a
    /// movie, a double-length episode) can't be added if it would blow past the upper bound
    /// (target*1.15) — that one pick is discarded (not the whole show) and another draw is
    /// tried. Keeps drawing until the running total reaches the actual target (not just the
    /// lower tolerance edge, target*0.85) — otherwise a large target would stop as soon as it
    /// barely crossed 85% of it, under-filling the queue and pulling from fewer distinct shows
    /// than the pool could otherwise support. If every show runs out before reaching the
    /// target, returns whatever was collected (MetLowerBound=false if that's still under the
    /// lower bound).
    /// </summary>
    public static QueueResult Build(LibraryStore store, double targetMinutes, Random? random = null)
    {
        random ??= new Random();
        var targetSeconds = targetMinutes * 60;
        var lowerBound = targetSeconds * (1 - ToleranceFraction);
        var upperBound = targetSeconds * (1 + ToleranceFraction);

        var eligibleFiles = store.Data.Files
            .Where(f => !f.IsExcluded)
            .Where(f => f.DurationSeconds.HasValue)
            .Where(f => !store.HasBeenPlayed(f.Path))
            .Where(f => !store.IsUnderExcludedFolder(f.Path))
            .Where(f => store.Data.Folders.Any(fo => string.Equals(fo.Path, f.FolderPath, StringComparison.OrdinalIgnoreCase) && !fo.IsExcluded))
            .ToList();

        var episodicFolderPaths = store.Data.Folders
            .Where(f => f.IsEpisodic && !f.IsExcluded)
            .Select(f => f.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Bonded multi-part groups, keyed by any file that belongs to one, so candidate-
        // building can look up "is this file part of a group?" in O(1).
        var fileToPartGroup = new Dictionary<FileEntry, List<FileEntry>>();
        foreach (var folderGroup in eligibleFiles.GroupBy(f => f.FolderPath, StringComparer.OrdinalIgnoreCase))
            foreach (var group in PartGroupDetector.FindGroups(folderGroup))
                foreach (var f in group)
                    fileToPartGroup[f] = group;

        // One list of "units" (a unit = a single file, or a bonded part-group) per show.
        var unitsByShow = new Dictionary<string, List<List<FileEntry>>>(StringComparer.OrdinalIgnoreCase);

        // Episodic shows offer exactly one unit: their earliest eligible episode (or its
        // bonded part-group, if it belongs to one).
        foreach (var folderPath in episodicFolderPaths)
        {
            var first = eligibleFiles
                .Where(f => string.Equals(f.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Path, NaturalPathComparer.Instance)
                .FirstOrDefault();
            if (first == null) continue;

            var unit = fileToPartGroup.TryGetValue(first, out var group) ? group : new List<FileEntry> { first };
            unitsByShow[folderPath] = new List<List<FileEntry>> { unit };
        }

        // Standalone shows offer every file as its own unit (bonded groups collapse to one).
        var addedGroups = new HashSet<List<FileEntry>>();
        foreach (var f in eligibleFiles.Where(f => !episodicFolderPaths.Contains(f.FolderPath)))
        {
            List<FileEntry> unit;
            if (fileToPartGroup.TryGetValue(f, out var group))
            {
                if (!addedGroups.Add(group)) continue;
                unit = group;
            }
            else
            {
                unit = new List<FileEntry> { f };
            }

            if (!unitsByShow.TryGetValue(f.FolderPath, out var list))
                unitsByShow[f.FolderPath] = list = new List<List<FileEntry>>();
            list.Add(unit);
        }

        // Shuffle each show's own units so, when that show's turn comes up, which specific
        // unit it offers is randomized too — not just insertion (natural/discovery) order.
        foreach (var list in unitsByShow.Values)
            Shuffle(list, random);

        var showsInPlay = unitsByShow.Keys.ToList();

        var result = new QueueResult();
        double runningTotal = 0;

        while (runningTotal < targetSeconds && showsInPlay.Count > 0)
        {
            var showIndex = random.Next(showsInPlay.Count);
            var units = unitsByShow[showsInPlay[showIndex]];

            var unit = units[^1];
            units.RemoveAt(units.Count - 1);
            if (units.Count == 0) showsInPlay.RemoveAt(showIndex);

            var prospectiveTotal = runningTotal + unit.Sum(f => f.DurationSeconds!.Value);
            if (prospectiveTotal > upperBound) continue; // this pick doesn't fit; the show stays in play if it has more

            foreach (var f in unit)
                result.Items.Add(new QueueItem { FilePath = f.Path, DurationSeconds = f.DurationSeconds!.Value });
            runningTotal = prospectiveTotal;
        }

        result.MetLowerBound = runningTotal >= lowerBound;
        return result;
    }

    private static void Shuffle<T>(IList<T> list, Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

/// <summary>
/// Compares paths by splitting into digit and non-digit runs, comparing digit runs
/// numerically. Makes "Season 2" sort before "Season 10" and "S01E2" before "S01E10".
/// </summary>
public sealed class NaturalPathComparer : IComparer<string>
{
    public static readonly NaturalPathComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x == null || y == null) return string.CompareOrdinal(x, y);

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                int startI = i, startJ = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;

                var numX = x.Substring(startI, i - startI).TrimStart('0');
                var numY = y.Substring(startJ, j - startJ).TrimStart('0');

                if (numX.Length != numY.Length)
                    return numX.Length - numY.Length;
                int cmp = string.CompareOrdinal(numX, numY);
                if (cmp != 0) return cmp;
            }
            else
            {
                int cmp = x[i].CompareTo(y[j]);
                if (cmp != 0) return cmp;
                i++;
                j++;
            }
        }

        return (x.Length - i) - (y.Length - j);
    }
}
