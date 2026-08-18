using System.Diagnostics;
using System.Globalization;
using System.IO;
using VlcFolderQueue.Data;

namespace VlcFolderQueue.Services;

public static class DurationProbe
{
    private static string? _ffprobePath;

    private static string? FindFfprobe()
    {
        if (_ffprobePath != null) return _ffprobePath;

        var candidate = Path.Combine(AppContext.BaseDirectory, "tools", "ffprobe.exe");
        _ffprobePath = File.Exists(candidate) ? candidate : null;
        return _ffprobePath;
    }

    /// <summary>
    /// Fills in DurationSeconds for any file that hasn't been probed yet.
    /// Silently leaves DurationSeconds null (file excluded from time-window math)
    /// if ffprobe isn't bundled or a given file can't be read.
    /// </summary>
    public static void ProbeMissingDurations(LibraryStore store)
    {
        var ffprobe = FindFfprobe();
        if (ffprobe == null) return;

        foreach (var file in store.Data.Files.Where(f => f.DurationSeconds == null))
        {
            file.DurationSeconds = TryGetDurationSeconds(ffprobe, file.Path);
        }
    }

    private static double? TryGetDurationSeconds(string ffprobePath, string filePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffprobePath,
                ArgumentList =
                {
                    "-v", "error",
                    "-show_entries", "format=duration",
                    "-of", "default=noprint_wrappers=1:nokey=1",
                    filePath
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            // Read both streams concurrently (not just stdout) so a chatty stderr can't fill
            // its pipe buffer and deadlock the process before it exits. Recovered/corrupted
            // files can make ffprobe hang outright rather than fail fast, so WaitForExit's
            // timeout has to be checked BEFORE blocking on the output — otherwise it's dead
            // code, and one hung file stalls every file queued behind it forever.
            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(15000))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already exited */ }
                return null;
            }

            var output = stdOutTask.GetAwaiter().GetResult().Trim();

            if (double.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                return seconds;
            return null;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
