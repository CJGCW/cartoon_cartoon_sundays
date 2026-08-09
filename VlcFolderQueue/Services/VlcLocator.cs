using Microsoft.Win32;
using System.IO;

namespace VlcFolderQueue.Services;

public static class VlcLocator
{
    private static readonly string[] FallbackPaths =
    {
        @"C:\Program Files\VideoLAN\VLC\vlc.exe",
        @"C:\Program Files (x86)\VideoLAN\VLC\vlc.exe"
    };

    public static string? FindVlcExecutable()
    {
        var fromRegistry = TryGetFromRegistry(Registry.LocalMachine, @"SOFTWARE\VideoLAN\VLC")
            ?? TryGetFromRegistry(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\VideoLAN\VLC");
        if (fromRegistry != null) return fromRegistry;

        return FallbackPaths.FirstOrDefault(File.Exists);
    }

    private static string? TryGetFromRegistry(RegistryKey root, string subKey)
    {
        try
        {
            using var key = root.OpenSubKey(subKey);
            var installDir = key?.GetValue("InstallDir") as string;
            if (string.IsNullOrEmpty(installDir)) return null;

            var exePath = Path.Combine(installDir, "vlc.exe");
            return File.Exists(exePath) ? exePath : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
