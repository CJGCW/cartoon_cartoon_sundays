using System.Diagnostics;
using System.IO;
using System.Text;

namespace VlcFolderQueue.Services;

public static class VlcLauncher
{
    /// <summary>
    /// Writes an M3U playlist for the given files (in order) and launches VLC with it.
    /// Returns false if VLC could not be located.
    /// </summary>
    public static bool WritePlaylistAndLaunch(IReadOnlyList<string> orderedFilePaths, out string? errorMessage)
    {
        var vlcPath = VlcLocator.FindVlcExecutable();
        if (vlcPath == null)
        {
            errorMessage = "Could not find vlc.exe. Please install VLC media player (videolan.org) and try again.";
            return false;
        }

        var playlistPath = Path.Combine(Path.GetTempPath(), $"VlcFolderQueue_{Guid.NewGuid():N}.m3u");
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        foreach (var path in orderedFilePaths)
            sb.AppendLine(path);
        File.WriteAllText(playlistPath, sb.ToString(), new UTF8Encoding(false));

        Process.Start(new ProcessStartInfo
        {
            FileName = vlcPath,
            ArgumentList = { playlistPath },
            UseShellExecute = false
        });

        errorMessage = null;
        return true;
    }
}
