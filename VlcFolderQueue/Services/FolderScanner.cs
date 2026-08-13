using System.IO;
using VlcFolderQueue.Data;

namespace VlcFolderQueue.Services;

public static class FolderScanner
{
    public static readonly string[] VideoExtensions =
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts"
    };

    /// <summary>
    /// Recursively scans every root folder and upserts discovered video files into the store.
    /// Files are grouped one level below their root — e.g. under "E:\rec\TV", each show folder
    /// (regardless of how many Season subfolders it has beneath it) becomes its own auto-discovered
    /// FolderEntry, and every file under that show folder (at any depth) is attributed to it. A file
    /// sitting directly in the root (no subfolder, e.g. a flat Movies root) is grouped under the root
    /// itself. This only handles two effective levels (root -> show); a root with an extra grouping
    /// level above shows (e.g. TV\Genre\Show\Season) would incorrectly lump a whole genre into one
    /// group — not part of the layouts this app currently targets.
    /// Per-folder failures (permissions, missing paths on damaged/recovered drives) are swallowed so
    /// one bad folder doesn't abort the scan. Files and discovered show/movie folders no longer found
    /// under a root are removed from the library — but only for roots that were actually reachable
    /// this run, so a temporarily unplugged/inaccessible root doesn't look like everything was deleted.
    /// </summary>
    public static void ScanIncludedFolders(LibraryStore store)
    {
        var now = DateTime.UtcNow;
        var knownFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scannedRootPaths = new List<string>();

        foreach (var root in store.Data.Folders.Where(f => f.IsRoot && !f.IsExcluded).ToList())
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root.Path, "*", SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                // Root unreachable this run (drive unplugged, permissions, etc.) — leave its
                // existing files/folders alone rather than treating them as deleted.
                continue;
            }

            scannedRootPaths.Add(root.Path);

            foreach (var file in files)
            {
                if (!VideoExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    continue;
                if (store.IsUnderExcludedFolder(file))
                    continue;

                var groupPath = GetGroupFolder(root.Path, file);
                if (!string.Equals(groupPath, root.Path, StringComparison.OrdinalIgnoreCase))
                    store.GetOrAddDiscoveredFolder(groupPath);

                knownFilePaths.Add(file);

                var existing = store.Data.Files.FirstOrDefault(f => string.Equals(f.Path, file, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    store.Data.Files.Add(new FileEntry
                    {
                        Path = file,
                        FolderPath = groupPath,
                        LastScannedUtc = now
                    });
                }
                else
                {
                    existing.FolderPath = groupPath;
                    existing.LastScannedUtc = now;
                }
            }
        }

        // Only prune within roots that were actually reachable this run, so a temporarily
        // unreachable root doesn't wipe out everything under it.
        bool WasScanned(string path) => scannedRootPaths.Any(r => IsPathUnderOrEqual(r, path));

        // Drop files that no longer exist on disk (moved/deleted since last scan).
        store.Data.Files.RemoveAll(f => WasScanned(f.Path) && !knownFilePaths.Contains(f.Path));

        // Drop discovered show/movie folders that no longer have any files under them
        // (the folder was deleted, renamed, or emptied on disk since the last scan).
        var foldersWithFiles = store.Data.Files.Select(f => f.FolderPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        store.Data.Folders.RemoveAll(f => !f.IsRoot && WasScanned(f.Path) && !foldersWithFiles.Contains(f.Path));
    }

    private static bool IsPathUnderOrEqual(string rootPath, string path)
    {
        var rootFull = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(path, rootFull, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetGroupFolder(string rootPath, string filePath)
    {
        var rootFull = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fileDir = Path.GetDirectoryName(filePath) ?? rootFull;

        if (string.Equals(fileDir, rootFull, StringComparison.OrdinalIgnoreCase))
            return rootFull;

        var relative = fileDir.Substring(rootFull.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var firstSegment = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return Path.Combine(rootFull, firstSegment);
    }
}
