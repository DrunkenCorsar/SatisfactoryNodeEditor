using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SatisfactoryNodeEditor.App.Services;

public static class WindowThemeService
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    private const int DarkCaptionColor = 0x00120F0B;
    private const int DarkBorderColor = 0x003D332A;
    private const int DarkCaptionTextColor = 0x00F7F4F2;

    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var useDarkMode = 1;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int));
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeBefore20H1, ref useDarkMode, sizeof(int));

        var captionColor = DarkCaptionColor;
        var borderColor = DarkBorderColor;
        var textColor = DarkCaptionTextColor;
        _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
        _ = DwmSetWindowAttribute(handle, DwmwaWindowBorderColor, ref borderColor, sizeof(int));
        _ = DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColor, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);
}
