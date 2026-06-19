using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SatisfactoryNodeEditor.App.Services;

namespace SatisfactoryNodeEditor.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplyWindowsAccentPalette();
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        base.OnExit(e);
    }

    private static void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.Color or UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
        {
            Current.Dispatcher.Invoke(ApplyWindowsAccentPalette);
        }
    }

    private static void ApplyWindowsAccentPalette()
    {
        WindowsThemePalette.Apply(Current.Resources);
        Current.Resources["WindowsAccentBrush"] = CreateBrush(WindowsAccentPalette.AccentColor);
        Current.Resources["WindowsAccentHoverBrush"] = CreateBrush(WindowsAccentPalette.AccentHoverColor);
        Current.Resources["WindowsAccentTextBrush"] = CreateBrush(WindowsAccentPalette.AccentTextColor);
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
