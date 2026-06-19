using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Win32;

namespace SatisfactoryNodeEditor.App.Services;

public static class WindowsAccentPalette
{
    public static Color AccentColor
    {
        get
        {
            var registryColor = TryReadPersonalizationAccentColor();
            if (registryColor.HasValue)
            {
                return registryColor.Value;
            }

            if (DwmGetColorizationColor(out var colorizationColor, out _) == 0)
            {
                return ColorFromArgbDword(colorizationColor);
            }

            return System.Windows.SystemColors.HighlightColor;
        }
    }

    public static Color AccentTextColor => IsLight(AccentColor) ? Colors.Black : Colors.White;

    public static Color AccentHoverColor => Mix(AccentColor, IsLight(AccentColor) ? Colors.Black : Colors.White, 0.14);

    private static bool IsLight(Color color)
    {
        var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        return luminance > 0.62;
    }

    private static Color Mix(Color first, Color second, double amount)
    {
        var inverse = 1 - amount;
        return Color.FromRgb(
            (byte)Math.Round(first.R * inverse + second.R * amount),
            (byte)Math.Round(first.G * inverse + second.G * amount),
            (byte)Math.Round(first.B * inverse + second.B * amount));
    }

    private static Color? TryReadPersonalizationAccentColor()
    {
        var value = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM")?.GetValue("AccentColor");
        return value is int accentColor ? ColorFromAbgrDword(unchecked((uint)accentColor)) : null;
    }

    private static Color ColorFromArgbDword(uint value) =>
        Color.FromRgb(
            (byte)((value >> 16) & 0xff),
            (byte)((value >> 8) & 0xff),
            (byte)(value & 0xff));

    private static Color ColorFromAbgrDword(uint value) =>
        Color.FromRgb(
            (byte)(value & 0xff),
            (byte)((value >> 8) & 0xff),
            (byte)((value >> 16) & 0xff));

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetColorizationColor(out uint colorizationColor, out bool colorizationOpaqueBlend);
}
