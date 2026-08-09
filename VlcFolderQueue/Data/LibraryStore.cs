using System.IO;
using System.Text.Json;

namespace VlcFolderQueue.Data;

/// <summary>
/// Plain-JSON persistence for the library (folders, files, play history) at
/// %AppData%\VlcFolderQueue\library.json. Loaded once into memory and saved
/// back after every mutation — the data set (a personal media library) is
/// small enough that this beats standing up SQLite for the same job.
/// </summary>
public class LibraryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public LibraryData Data { get; private set; } = new();

    public LibraryStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VlcFolderQueue");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "library.json");
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            Data = new LibraryData();
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            Data = JsonSerializer.Deserialize<LibraryData>(json, JsonOptions) ?? new LibraryData();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Data = new LibraryData();
        }

        MigrateRootFlag();
    }

    /// <summary>
    /// Data saved before the show/season-aware scanner (which introduced IsRoot) has every folder
    /// deserialize with IsRoot=false. A folder with no other library folder as a path-ancestor can't
    /// be an auto-discovered show (those are always nested under a root), so it must have been
    /// explicitly added — self-heal it back to a root.
    /// </summary>
    private void MigrateRootFlag()
    {
        foreach (var folder in Data.Folders.Where(f => !f.IsRoot))
        {
            var hasAncestor = Data.Folders.Any(other =>
                !ReferenceEquals(other, folder) &&
                folder.Path.StartsWith(
                    other.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase));

            if (!hasAncestor)
                folder.IsRoot = true;
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Data, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    public FolderEntry? GetRoot() => Data.Folders.FirstOrDefault(f => f.IsRoot);

    /// <summary>
    /// Only one root ever exists. Replacing it removes the previous root, every show the
    /// scanner discovered under it, and their file entries (play history is untouched — it's
    /// keyed by absolute file path, independent of folder tracking).
    /// </summary>
    public FolderEntry ReplaceRoot(string path)
    {
        var existingRoot = GetRoot();
        if (existingRoot != null)
            RemoveFolderAndDescendants(existingRoot.Path);

        var entry = new FolderEntry { Path = path, IsRoot = true };
        Data.Folders.Add(entry);
        return entry;
    }

    /// <summary>Gets or creates a folder auto-discovered by the scanner (a show/movie group under the root).</summary>
    public FolderEntry GetOrAddDiscoveredFolder(string path)
    {
        var existing = Data.Folders.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        var entry = new FolderEntry { Path = path };
        Data.Folders.Add(entry);
        return entry;
    }

    private void RemoveFolderAndDescendants(string folderPath)
    {
        var prefix = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        Data.Folders.RemoveAll(f =>
            string.Equals(f.Path, folderPath, StringComparison.OrdinalIgnoreCase) ||
            f.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        Data.Files.RemoveAll(f =>
            string.Equals(f.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase) ||
            f.FolderPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsUnderExcludedFolder(string filePath)
    {
        foreach (var folder in Data.Folders.Where(f => f.IsExcluded))
        {
            var prefix = folder.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(filePath, folder.Path, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public bool HasBeenPlayed(string filePath) =>
        Data.PlayHistory.Any(h => string.Equals(h.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

    public void MarkPlayed(IEnumerable<string> filePaths)
    {
        var now = DateTime.UtcNow;
        foreach (var path in filePaths)
        {
            var existing = Data.PlayHistory.FirstOrDefault(h => string.Equals(h.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                existing.PlayedUtc = now;
            else
                Data.PlayHistory.Add(new PlayHistoryEntry { FilePath = path, PlayedUtc = now });
        }
    }

    public void MarkUnplayed(string filePath)
    {
        Data.PlayHistory.RemoveAll(h => string.Equals(h.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
    }
}
