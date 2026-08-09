namespace VlcFolderQueue.Data;

public class FolderEntry
{
    public string Path { get; set; } = "";
    public bool IsRoot { get; set; }
    public bool IsExcluded { get; set; }
    public bool IsEpisodic { get; set; }
    public List<string> Tags { get; set; } = new();
}

public class FileEntry
{
    public string Path { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public bool IsExcluded { get; set; }
    public double? DurationSeconds { get; set; }
    public DateTime LastScannedUtc { get; set; }
}

public class PlayHistoryEntry
{
    public string FilePath { get; set; } = "";
    public DateTime PlayedUtc { get; set; }
}

public class LibraryData
{
    public List<FolderEntry> Folders { get; set; } = new();
    public List<FileEntry> Files { get; set; } = new();
    public List<PlayHistoryEntry> PlayHistory { get; set; } = new();
}
