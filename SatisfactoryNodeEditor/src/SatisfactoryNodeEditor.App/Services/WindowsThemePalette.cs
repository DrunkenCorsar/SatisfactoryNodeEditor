using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SatisfactoryNodeEditor.App.Services;

public static class WindowsThemePalette
{
    public static bool IsDarkMode
    {
        get => true;
    }

    public static void Apply(ResourceDictionary resources)
    {
        if (SystemParameters.HighContrast)
        {
            ApplyHighContrast(resources);
            return;
        }

        ApplyDark(resources);
    }

    private static void ApplyLight(ResourceDictionary resources)
    {
        Set(resources, "AppWindowBrush", Color.FromRgb(243, 244, 246));
        Set(resources, "AppPanelBrush", Color.FromRgb(255, 255, 255));
        Set(resources, "AppPanelAltBrush", Color.FromRgb(248, 250, 252));
        Set(resources, "AppInputBrush", Color.FromRgb(255, 255, 255));
        Set(resources, "AppBorderBrush", Color.FromRgb(205, 213, 223));
        Set(resources, "AppTextBrush", Color.FromRgb(17, 24, 39));
        Set(resources, "AppSecondaryTextBrush", Color.FromRgb(75, 85, 99));
        Set(resources, "AppMutedTextBrush", Color.FromRgb(107, 114, 128));
    }

    private static void ApplyDark(ResourceDictionary resources)
    {
        Set(resources, "AppWindowBrush", Color.FromRgb(11, 15, 18));
        Set(resources, "AppPanelBrush", Color.FromRgb(17, 24, 32));
        Set(resources, "AppPanelAltBrush", Color.FromRgb(21, 29, 38));
        Set(resources, "AppInputBrush", Color.FromRgb(14, 20, 27));
        Set(resources, "AppBorderBrush", Color.FromRgb(42, 51, 61));
        Set(resources, "AppTextBrush", Color.FromRgb(242, 244, 247));
        Set(resources, "AppSecondaryTextBrush", Color.FromRgb(170, 180, 192));
        Set(resources, "AppMutedTextBrush", Color.FromRgb(111, 122, 134));
    }

    private static void ApplyHighContrast(ResourceDictionary resources)
    {
        resources["AppWindowBrush"] = SystemColors.WindowBrush;
        resources["AppPanelBrush"] = SystemColors.ControlBrush;
        resources["AppPanelAltBrush"] = SystemColors.ControlBrush;
        resources["AppInputBrush"] = SystemColors.WindowBrush;
        resources["AppBorderBrush"] = SystemColors.ActiveBorderBrush;
        resources["AppTextBrush"] = SystemColors.WindowTextBrush;
        resources["AppSecondaryTextBrush"] = SystemColors.GrayTextBrush;
        resources["AppMutedTextBrush"] = SystemColors.GrayTextBrush;
    }

    private static void Set(ResourceDictionary resources, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        resources[key] = brush;
    }
}
