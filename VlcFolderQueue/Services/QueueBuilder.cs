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
    /// non-excluded, duration known). Episodic only changes WHICH file represents a show
    /// in the draw: instead of every episode being its own candidate, a show marked
    /// Episodic contributes a single candidate — its earliest not-yet-played episode
    /// (natural/numeric order) — so picking that show queues up "the next one", not a
    /// random episode and not the whole season/series as a block. That candidate competes
    /// in the same one-at-a-time random draw as standalone files.
    /// Adds candidates one at a time, checking the resulting total *before* committing each
    /// one (not just once already past the lower bound) so a single long file (a movie, a
    /// double-length episode) can't be added if it would blow past the upper bound — it's
    /// skipped in favor of something that still fits. Stops once the total lands within
    /// [target*0.85, target*1.15]; if nothing more fits, returns whatever was collected
    /// (MetLowerBound=false if that's still under the lower bound).
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

        // One candidate per episodic show: its earliest eligible (unplayed) episode in
        // natural order — "Season 2" before "Season 10", "E2" before "E10".
        var episodicCandidates = eligibleFiles
            .Where(f => episodicFolderPaths.Contains(f.FolderPath))
            .GroupBy(f => f.FolderPath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(f => f.Path, NaturalPathComparer.Instance).First());

        var standaloneCandidates = eligibleFiles.Where(f => !episodicFolderPaths.Contains(f.FolderPath));

        var pool = standaloneCandidates.Concat(episodicCandidates)
            .OrderBy(_ => random.Next())
            .ToList();

        var result = new QueueResult();
        double runningTotal = 0;

        foreach (var candidate in pool)
        {
            if (runningTotal >= lowerBound) break;

            var prospectiveTotal = runningTotal + candidate.DurationSeconds!.Value;
            if (prospectiveTotal > upperBound) continue;

            result.Items.Add(new QueueItem { FilePath = candidate.Path, DurationSeconds = candidate.DurationSeconds!.Value });
            runningTotal = prospectiveTotal;
        }

        result.MetLowerBound = runningTotal >= lowerBound;
        return result;
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
