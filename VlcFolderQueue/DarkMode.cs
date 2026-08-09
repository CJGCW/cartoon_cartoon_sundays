using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VlcFolderQueue;

/// <summary>
/// Tells DWM this window is dark-themed. Even with WindowStyle="None" and a zero-thickness
/// WindowChrome (WPF owns all client-area rendering), DWM still composites a thin native
/// resize-border/shadow outside the client area, and without this flag it renders light by
/// default regardless of what WPF draws inside the window.
/// </summary>
public static class DarkMode
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            int useDarkMode = 1;
            try
            {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
            }
            catch (DllNotFoundException)
            {
                // Very old Windows builds without dwmapi.dll support for this attribute.
            }
        };
    }
}
