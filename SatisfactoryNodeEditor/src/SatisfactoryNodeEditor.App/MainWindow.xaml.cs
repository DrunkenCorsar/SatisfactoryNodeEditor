using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.IO;
using Microsoft.Win32;
using SatisfactoryNodeEditor.App.Services;
using SatisfactoryNodeEditor.App.ViewModels;

namespace SatisfactoryNodeEditor.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        var coordinateConverter = new SatisfactoryMapCoordinateConverter();
        DataContext = new MainViewModel(
            new ExternalSaveMutationService(),
            new SaveFileDialogService(),
            new ResourceNodeInspectionService(coordinateConverter),
            new ResourceNodeShuffleService());
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        WindowThemeService.Apply(this);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        WindowThemeService.Apply(this);
        Dispatcher.BeginInvoke(FocusStartupSink, DispatcherPriority.ApplicationIdle);
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
    }

    private void FocusStartupSink()
    {
        InitialFocusSink.Focus();
        Keyboard.Focus(InitialFocusSink);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.Color or UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
        {
            Dispatcher.Invoke(() => WindowThemeService.Apply(this));
        }
    }

    private void WelcomeDropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDroppedSavePath(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void WelcomeDropZone_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && TryGetDroppedSavePath(e, out var savePath))
        {
            await viewModel.LoadSaveAsync(savePath);
        }

        e.Handled = true;
    }

    private static bool TryGetDroppedSavePath(DragEventArgs e, out string savePath)
    {
        savePath = string.Empty;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return false;
        }

        savePath = files.FirstOrDefault(file => Path.GetExtension(file).Equals(".sav", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(savePath) && File.Exists(savePath);
    }
}
