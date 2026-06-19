using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using SatisfactoryNodeEditor.App.Models;
using SatisfactoryNodeEditor.App.Services;

namespace SatisfactoryNodeEditor.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ISaveMutationService _saveMutationService;
    private readonly SaveFileDialogService _saveFileDialogService;
    private readonly ResourceNodeInspectionService _resourceNodeInspectionService;
    private readonly ResourceNodeShuffleService _resourceNodeShuffleService;
    private string _inputSavePath = string.Empty;
    private string _outputSavePath = string.Empty;
    private string _logText = string.Empty;
    private string _loadErrorMessage = string.Empty;
    private string _guideImageOverlaySource = string.Empty;
    private bool _isCompatibilityGuideVisible;
    private bool _isSafetyNoticeVisible = !HasAcceptedSafetyNotice();
    private bool _showDefaultResourceCounts;
    private bool _isBusy;
    private bool _hasUnsavedShuffle;
    private bool _hasLoadedSave;
    private bool _suppressResourceCountLogging;
    private ObservableCollection<ResourceNodeViewModel> _resourceNodes = [];
    private Dictionary<string, int> _initialResourceCounts = [];
    private readonly Dictionary<string, int> _lastLoggedResourceCounts = new(StringComparer.OrdinalIgnoreCase);
    private double _clusteringValue = 30;
    private string _selectedEditorTab = "Shuffle";
    private bool _isHardModeEnabled;
    private bool _isBrushResourceChangeEnabled;
    private bool _isBrushPurityChangeEnabled;
    private string _brushResourceType = "Iron";
    private string _brushPurity = "Normal";
    private double _brushSizePixels = 32;
    private PurityDistributionMode _selectedPurityDistributionMode = PurityDistributionMode.Native;

    public MainViewModel(
        ISaveMutationService saveMutationService,
        SaveFileDialogService saveFileDialogService,
        ResourceNodeInspectionService resourceNodeInspectionService,
        ResourceNodeShuffleService resourceNodeShuffleService)
    {
        _saveMutationService = saveMutationService;
        _saveFileDialogService = saveFileDialogService;
        _resourceNodeInspectionService = resourceNodeInspectionService;
        _resourceNodeShuffleService = resourceNodeShuffleService;
        ReadSaveCommand = new RelayCommand(ReadSaveAsync, () => !IsBusy);
        UseTemplateCommand = new RelayCommand(UseTemplateAsync, () => !IsBusy);
        ShuffleCommand = new RelayCommand(ShuffleAsync, () => !IsBusy && !IsNodeCountOverMaximum && ResourceNodes.Any(node => node.NodeKind.Equals("ResourceNode", StringComparison.OrdinalIgnoreCase)));
        ShufflePuritiesCommand = new RelayCommand(ShufflePuritiesAsync, () => !IsBusy && ResourceNodes.Any(IsOrdinaryNonEmptyResourceNode));
        SaveCommand = new RelayCommand(SaveAsync, () => !IsBusy && _hasUnsavedShuffle && File.Exists(InputSavePath));
        OpenOutputCommand = new RelayCommand(OpenOutputAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(OutputSavePath));
        ResetResourceCountsCommand = new RelayCommand(ResetResourceCountsToDefaultAsync);
        ResetInitialResourceCountsCommand = new RelayCommand(ResetResourceCountsToInitialAsync);
        IncrementResourceCountCommand = new RelayCommand<ResourceCountOption>(IncrementResourceCountAsync);
        DecrementResourceCountCommand = new RelayCommand<ResourceCountOption>(DecrementResourceCountAsync);
        NodeEditedCommand = new RelayCommand<ResourceNodeViewModel>(NodeEditedAsync);
        SetPurityDistributionModeCommand = new RelayCommand<string>(SetPurityDistributionModeAsync);
        SetEditorTabCommand = new RelayCommand<string>(SetEditorTabAsync);
        CopyLogsCommand = new RelayCommand(CopyLogsAsync, () => !string.IsNullOrWhiteSpace(LogText));
        ClearLogCommand = new RelayCommand(ClearLogAsync);
        SupportCommand = new RelayCommand(OpenSupportAsync);
        OpenGitHubCommand = new RelayCommand(OpenGitHubAsync);
        AcceptSafetyNoticeCommand = new RelayCommand(AcceptSafetyNoticeAsync);
        ToggleCompatibilityGuideCommand = new RelayCommand(ToggleCompatibilityGuideAsync);
        ToggleGuideImageCommand = new RelayCommand<string>(ToggleGuideImageAsync);
        InitializePerResourcePurityDistributions();
        ResetResourceCounts(CreateDefaultResourceCounts().ToDictionary(option => option.DisplayName, option => option.Count, StringComparer.OrdinalIgnoreCase));
        AppendLog("App started", "Select a .sav file or use the bundled template to begin.");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand ReadSaveCommand { get; }
    public RelayCommand UseTemplateCommand { get; }
    public RelayCommand ShuffleCommand { get; }
    public RelayCommand ShufflePuritiesCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand OpenOutputCommand { get; }
    public RelayCommand ResetResourceCountsCommand { get; }
    public RelayCommand ResetInitialResourceCountsCommand { get; }
    public RelayCommand<ResourceCountOption> IncrementResourceCountCommand { get; }
    public RelayCommand<ResourceCountOption> DecrementResourceCountCommand { get; }
    public RelayCommand<ResourceNodeViewModel> NodeEditedCommand { get; }
    public RelayCommand<string> SetPurityDistributionModeCommand { get; }
    public RelayCommand<string> SetEditorTabCommand { get; }
    public RelayCommand CopyLogsCommand { get; }
    public RelayCommand ClearLogCommand { get; }
    public RelayCommand SupportCommand { get; }
    public RelayCommand OpenGitHubCommand { get; }
    public RelayCommand AcceptSafetyNoticeCommand { get; }
    public RelayCommand ToggleCompatibilityGuideCommand { get; }
    public RelayCommand<string> ToggleGuideImageCommand { get; }
    public ObservableCollection<ResourceCountOption> ResourceCounts { get; } = [];
    public PurityDistributionViewModel GlobalPurityDistribution { get; } = new();
    public ObservableCollection<ResourcePurityDistributionViewModel> PerResourcePurityDistributions { get; } = [];
    public ObservableCollection<ResourceNodeViewModel> ResourceNodes
    {
        get => _resourceNodes;
        private set
        {
            if (SetField(ref _resourceNodes, value))
            {
                ShuffleCommand.RaiseCanExecuteChanged();
                ShufflePuritiesCommand.RaiseCanExecuteChanged();
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string InputSavePath
    {
        get => _inputSavePath;
        private set
        {
            if (SetField(ref _inputSavePath, value))
            {
                OnPropertyChanged(nameof(InputSaveFileName));
            }
        }
    }

    public string InputSaveFileName => string.IsNullOrWhiteSpace(InputSavePath)
        ? "No save selected"
        : Path.GetFileName(InputSavePath);

    public string OutputSavePath
    {
        get => _outputSavePath;
        private set
        {
            if (SetField(ref _outputSavePath, value))
            {
                OnPropertyChanged(nameof(OutputSaveFileName));
            }
        }
    }

    public string OutputSaveFileName => string.IsNullOrWhiteSpace(OutputSavePath)
        ? "No output prepared"
        : Path.GetFileName(OutputSavePath);

    public string LogText
    {
        get => _logText;
        private set
        {
            if (SetField(ref _logText, value))
            {
                CopyLogsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LoadErrorMessage
    {
        get => _loadErrorMessage;
        private set
        {
            if (SetField(ref _loadErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasLoadError));
            }
        }
    }

    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadErrorMessage);

    public string GuideImageOverlaySource
    {
        get => _guideImageOverlaySource;
        private set
        {
            if (SetField(ref _guideImageOverlaySource, value))
            {
                OnPropertyChanged(nameof(IsGuideImageOverlayVisible));
            }
        }
    }

    public bool IsGuideImageOverlayVisible => !string.IsNullOrWhiteSpace(GuideImageOverlaySource);

    public bool IsCompatibilityGuideVisible
    {
        get => _isCompatibilityGuideVisible;
        private set => SetField(ref _isCompatibilityGuideVisible, value);
    }

    public bool IsSafetyNoticeVisible
    {
        get => _isSafetyNoticeVisible;
        private set => SetField(ref _isSafetyNoticeVisible, value);
    }

    public double ClusteringValue
    {
        get => _clusteringValue;
        set
        {
            var normalized = Math.Clamp(value, 0, 100);
            if (SetField(ref _clusteringValue, normalized))
            {
                if (!IsHardModeAvailable)
                {
                    IsHardModeEnabled = false;
                }

                OnPropertyChanged(nameof(ClusteringDisplay));
                OnPropertyChanged(nameof(IsHardModeAvailable));
            }
        }
    }

    public string ClusteringDisplay => $"{ClusteringValue:0}%";

    public PurityDistributionMode SelectedPurityDistributionMode
    {
        get => _selectedPurityDistributionMode;
        private set
        {
            if (SetField(ref _selectedPurityDistributionMode, value))
            {
                OnPropertyChanged(nameof(IsNativePurityMode));
                OnPropertyChanged(nameof(IsGlobalPurityMode));
                OnPropertyChanged(nameof(IsPerResourcePurityMode));
                OnPropertyChanged(nameof(IsPerResourcePurityPanelVisible));
                OnPropertyChanged(nameof(NativeModeButtonBrush));
                OnPropertyChanged(nameof(GlobalModeButtonBrush));
                OnPropertyChanged(nameof(PerResourceModeButtonBrush));
                OnPropertyChanged(nameof(NativeModeTextBrush));
                OnPropertyChanged(nameof(GlobalModeTextBrush));
                OnPropertyChanged(nameof(PerResourceModeTextBrush));
            }
        }
    }

    public bool IsNativePurityMode => SelectedPurityDistributionMode == PurityDistributionMode.Native;

    public bool IsGlobalPurityMode => SelectedPurityDistributionMode == PurityDistributionMode.Global;

    public bool IsPerResourcePurityMode => SelectedPurityDistributionMode == PurityDistributionMode.PerResource;

    public Brush NativeModeButtonBrush => GetModeButtonBrush(IsNativePurityMode);

    public Brush GlobalModeButtonBrush => GetModeButtonBrush(IsGlobalPurityMode);

    public Brush PerResourceModeButtonBrush => GetModeButtonBrush(IsPerResourcePurityMode);

    public Brush NativeModeTextBrush => GetModeTextBrush(IsNativePurityMode);

    public Brush GlobalModeTextBrush => GetModeTextBrush(IsGlobalPurityMode);

    public Brush PerResourceModeTextBrush => GetModeTextBrush(IsPerResourcePurityMode);

    public string SelectedEditorTab
    {
        get => _selectedEditorTab;
        private set
        {
            if (SetField(ref _selectedEditorTab, value))
            {
                OnPropertyChanged(nameof(IsShuffleTabSelected));
                OnPropertyChanged(nameof(IsMapBrushTabSelected));
                OnPropertyChanged(nameof(IsAboutTabSelected));
                OnPropertyChanged(nameof(IsPerResourcePurityPanelVisible));
                OnPropertyChanged(nameof(ShuffleTabBrush));
                OnPropertyChanged(nameof(MapBrushTabBrush));
                OnPropertyChanged(nameof(AboutTabBrush));
                OnPropertyChanged(nameof(ShuffleTabTextBrush));
                OnPropertyChanged(nameof(MapBrushTabTextBrush));
                OnPropertyChanged(nameof(AboutTabTextBrush));
            }
        }
    }

    public bool IsShuffleTabSelected => SelectedEditorTab.Equals("Shuffle", StringComparison.OrdinalIgnoreCase);

    public bool IsMapBrushTabSelected => SelectedEditorTab.Equals("MapBrush", StringComparison.OrdinalIgnoreCase);

    public bool IsAboutTabSelected => SelectedEditorTab.Equals("About", StringComparison.OrdinalIgnoreCase);

    public bool IsPerResourcePurityPanelVisible => IsShuffleTabSelected && IsPerResourcePurityMode;

    public string AppVersion { get; } = GetDisplayVersion();

    public string WindowTitle => $"Satisfactory Node Editor v{AppVersion}";

    public Brush ShuffleTabBrush => GetTabButtonBrush(IsShuffleTabSelected);

    public Brush MapBrushTabBrush => GetTabButtonBrush(IsMapBrushTabSelected);

    public Brush AboutTabBrush => GetTabButtonBrush(IsAboutTabSelected);

    public Brush ShuffleTabTextBrush => GetTabTextBrush(IsShuffleTabSelected);

    public Brush MapBrushTabTextBrush => GetTabTextBrush(IsMapBrushTabSelected);

    public Brush AboutTabTextBrush => GetTabTextBrush(IsAboutTabSelected);

    public int CurrentTotalNodes => ResourceCounts.Sum(option => option.Count);

    public int ExpectedTotalNodes => DefaultTotalNodes;

    public string TotalNodesDisplay => $"Total nodes: {CurrentTotalNodes}/{ExpectedTotalNodes}";

    public bool IsNodeCountOverMaximum => CurrentTotalNodes > ExpectedTotalNodes;

    public Brush TotalNodesBrush => CurrentTotalNodes == ExpectedTotalNodes
        ? Brushes.ForestGreen
        : CurrentTotalNodes < ExpectedTotalNodes
            ? Brushes.Goldenrod
            : Brushes.Firebrick;

    public bool ShouldShowInitialResetButton => _initialResourceCounts.Count > 0 && !CountsMatch(_initialResourceCounts, DefaultCountMap);

    public bool ShowDefaultResourceCounts
    {
        get => _showDefaultResourceCounts;
        set => SetField(ref _showDefaultResourceCounts, value);
    }

    public bool HasLoadedSave
    {
        get => _hasLoadedSave;
        private set
        {
            if (SetField(ref _hasLoadedSave, value))
            {
                OnPropertyChanged(nameof(IsWelcomeVisible));
            }
        }
    }

    public bool IsWelcomeVisible => !HasLoadedSave;

    public bool IsHardModeAvailable => ClusteringValue >= 99.5;

    public bool IsHardModeEnabled
    {
        get => _isHardModeEnabled;
        set => SetField(ref _isHardModeEnabled, value);
    }

    public bool IsBrushResourceChangeEnabled
    {
        get => _isBrushResourceChangeEnabled;
        set => SetField(ref _isBrushResourceChangeEnabled, value);
    }

    public bool IsBrushPurityChangeEnabled
    {
        get => _isBrushPurityChangeEnabled;
        set => SetField(ref _isBrushPurityChangeEnabled, value);
    }

    public string BrushResourceType
    {
        get => _brushResourceType;
        set => SetField(ref _brushResourceType, value);
    }

    public string BrushPurity
    {
        get => _brushPurity;
        set => SetField(ref _brushPurity, value);
    }

    public double BrushSizePixels
    {
        get => _brushSizePixels;
        set
        {
            var normalized = Math.Clamp(value, 8, 128);
            if (SetField(ref _brushSizePixels, normalized))
            {
                OnPropertyChanged(nameof(BrushSizeDisplay));
            }
        }
    }

    public string BrushSizeDisplay => $"{BrushSizePixels:0} px";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                ReadSaveCommand.RaiseCanExecuteChanged();
                UseTemplateCommand.RaiseCanExecuteChanged();
                ShuffleCommand.RaiseCanExecuteChanged();
                ShufflePuritiesCommand.RaiseCanExecuteChanged();
                SaveCommand.RaiseCanExecuteChanged();
                OpenOutputCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private async Task ReadSaveAsync()
    {
        var selectedPath = _saveFileDialogService.PickSaveFile();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            AppendLog("Load save cancelled", "No file was selected.");
            return;
        }

        await LoadSaveAsync(selectedPath);
    }

    private async Task UseTemplateAsync()
    {
        var bundledTemplatePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Templates", "NodeEditor_Template.sav");
        if (!File.Exists(bundledTemplatePath))
        {
            LoadErrorMessage = "Template save could not be found. Try rebuilding the app or load your own save file.";
            AppendLog("Template load failed", LoadErrorMessage);
            return;
        }

        AppendLog("Template selected", $"Using bundled template without copying it:{Environment.NewLine}{bundledTemplatePath}");
        await LoadSaveAsync(bundledTemplatePath);
    }

    public async Task LoadSaveAsync(string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
        {
            AppendLog("Load save skipped", $"Path is empty or missing:{Environment.NewLine}{selectedPath}");
            return;
        }

        if (!Path.GetExtension(selectedPath).Equals(".sav", StringComparison.OrdinalIgnoreCase))
        {
            LoadErrorMessage = "Only Satisfactory .sav files can be loaded.";
            AppendLog("Load save skipped", $"""
                {LoadErrorMessage}

                Selected path:
                {selectedPath}
                """);
            return;
        }

        var previousInputSavePath = InputSavePath;
        var previousOutputSavePath = OutputSavePath;
        var previousHasLoadedSave = HasLoadedSave;
        var previousHasUnsavedShuffle = _hasUnsavedShuffle;
        InputSavePath = selectedPath;
        OutputSavePath = BuildOutputPath(selectedPath);
        if (!IsBundledTemplatePath(selectedPath))
        {
            _saveFileDialogService.RememberInputPath(selectedPath);
        }
        _hasUnsavedShuffle = false;
        LoadErrorMessage = string.Empty;
        AppendLog("Load save started", $"""
            Input:
            {InputSavePath}

            Prepared output:
            {OutputSavePath}
            """);
        ShuffleCommand.RaiseCanExecuteChanged();
        SaveCommand.RaiseCanExecuteChanged();
        OpenOutputCommand.RaiseCanExecuteChanged();

        IsBusy = true;
        try
        {
            var inspection = await _resourceNodeInspectionService.InspectNodesAsync(InputSavePath);
            if (inspection.Success)
            {
                ResourceNodes = new ObservableCollection<ResourceNodeViewModel>(inspection.Nodes);
                _initialResourceCounts = BuildCountsFromSave(inspection.Nodes);
                ResetResourceCounts(_initialResourceCounts);
                SyncPurityDistributionsFromNodes();
                _hasUnsavedShuffle = false;
                SaveCommand.RaiseCanExecuteChanged();
                HasLoadedSave = true;
                OnPropertyChanged(nameof(ShouldShowInitialResetButton));
            }
            else
            {
                if (previousHasLoadedSave)
                {
                    InputSavePath = previousInputSavePath;
                    OutputSavePath = previousOutputSavePath;
                    _hasUnsavedShuffle = previousHasUnsavedShuffle;
                    HasLoadedSave = true;
                }
                else
                {
                    ResourceNodes = [];
                    _hasUnsavedShuffle = false;
                    HasLoadedSave = false;
                }

                LoadErrorMessage = inspection.ErrorMessage ?? "This save file cannot be loaded by the current node editor.";
                SaveCommand.RaiseCanExecuteChanged();
            }

            AppendLog(inspection.Success ? "Load save completed" : "Load save failed", $"""
                Input:
                {selectedPath}

                Prepared output:
                {BuildOutputPath(selectedPath)}

                Loaded state: {HasLoadedSave}
                Nodes in view: {ResourceNodes.Count}
                Ordinary nodes: {ResourceNodes.Count(node => node.NodeKind.Equals("ResourceNode", StringComparison.OrdinalIgnoreCase))}
                Wells: {ResourceNodes.Count(node => node.NodeKind.Equals("Well", StringComparison.OrdinalIgnoreCase))}
                Geysers: {ResourceNodes.Count(node => node.NodeKind.Equals("Geyser", StringComparison.OrdinalIgnoreCase))}
                Unsaved changes: {_hasUnsavedShuffle}

                {inspection.Log}
                {inspection.ErrorMessage}
                """);

        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task ToggleCompatibilityGuideAsync()
    {
        IsCompatibilityGuideVisible = !IsCompatibilityGuideVisible;
        return Task.CompletedTask;
    }

    private Task AcceptSafetyNoticeAsync()
    {
        try
        {
            var path = GetSafetyNoticeAcceptedPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, DateTimeOffset.Now.ToString("O"));
        }
        catch (Exception ex)
        {
            AppendLog("Safety notice acceptance could not be saved", ex.Message);
        }

        IsSafetyNoticeVisible = false;
        return Task.CompletedTask;
    }

    private Task ToggleGuideImageAsync(string? imageSource)
    {
        if (string.IsNullOrWhiteSpace(imageSource) || GuideImageOverlaySource.Equals(imageSource, StringComparison.OrdinalIgnoreCase))
        {
            GuideImageOverlaySource = string.Empty;
        }
        else
        {
            GuideImageOverlaySource = imageSource;
        }

        return Task.CompletedTask;
    }

    private async Task ShuffleAsync()
    {
        AppendLog("Shuffle started", $"""
            Clustering: {ClusteringValue:0}%
            Hard mode: {(IsHardModeEnabled && IsHardModeAvailable ? "on" : "off")}
            Requested resource counts: {FormatResourceCounts()}
            Total nodes: {CurrentTotalNodes}/{ExpectedTotalNodes}
            Purity settings: {FormatPuritySettingsSummary()}
            """);
        IsBusy = true;
        try
        {
            var snapshot = ResourceNodes.ToArray();
            var clusteringValue = ClusteringValue;
            var hardMode = IsHardModeEnabled && IsHardModeAvailable;
            var requestedCounts = ResourceCounts.ToDictionary(option => option.DisplayName, option => option.Count, StringComparer.OrdinalIgnoreCase);
            var puritySettings = BuildPurityShuffleSettings();
            var result = await Task.Run(() => _resourceNodeShuffleService.Shuffle(snapshot, clusteringValue, hardMode, requestedCounts, puritySettings));
            ResourceNodes = new ObservableCollection<ResourceNodeViewModel>(snapshot);
            SyncPurityDistributionsFromNodes();
            _hasUnsavedShuffle = _hasUnsavedShuffle || result.NodesChanged > 0;
            SaveCommand.RaiseCanExecuteChanged();
            AppendLog("Shuffle completed", $"""
                Nodes changed in preview: {result.NodesChanged}
                Unsaved changes: {_hasUnsavedShuffle}

                {result.Log}
                """);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ShufflePuritiesAsync()
    {
        AppendLog("Purity shuffle started", $"""
            Purity settings: {FormatPuritySettingsSummary()}
            Ordinary non-empty nodes: {ResourceNodes.Count(IsOrdinaryNonEmptyResourceNode)}
            """);
        IsBusy = true;
        try
        {
            var snapshot = ResourceNodes.ToArray();
            var puritySettings = BuildPurityShuffleSettings();
            var result = await Task.Run(() => _resourceNodeShuffleService.ShufflePurities(snapshot, puritySettings));
            ResourceNodes = new ObservableCollection<ResourceNodeViewModel>(snapshot);
            SyncPurityDistributionsFromNodes();
            _hasUnsavedShuffle = _hasUnsavedShuffle || result.NodesChanged > 0;
            SaveCommand.RaiseCanExecuteChanged();
            AppendLog("Purity shuffle completed", $"""
                Nodes changed in preview: {result.NodesChanged}
                Unsaved changes: {_hasUnsavedShuffle}

                {result.Log}
                """);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveAsync()
    {
        var selectedOutputPath = _saveFileDialogService.PickOutputSaveFile(OutputSavePath);
        if (string.IsNullOrWhiteSpace(selectedOutputPath))
        {
            AppendLog("Save cancelled", "No output path was selected.");
            return;
        }

        if (!Path.GetExtension(selectedOutputPath).Equals(".sav", StringComparison.OrdinalIgnoreCase))
        {
            AppendLog("Save cancelled", $"""
                Output file must use the .sav extension.

                Selected path:
                {selectedOutputPath}
                """);
            return;
        }

        OutputSavePath = selectedOutputPath;
        OpenOutputCommand.RaiseCanExecuteChanged();

        IsBusy = true;
        try
        {
            AppendLog("Save started", $"""
                Input:
                {InputSavePath}

                Output:
                {OutputSavePath}

                Assignments to persist:
                Ordinary nodes: {ResourceNodes.Count(node => node.NodeKind.Equals("ResourceNode", StringComparison.OrdinalIgnoreCase))}
                Wells: {ResourceNodes.Count(node => node.NodeKind.Equals("Well", StringComparison.OrdinalIgnoreCase))}
                Geysers ignored: {ResourceNodes.Count(node => node.NodeKind.Equals("Geyser", StringComparison.OrdinalIgnoreCase))}
                Unsaved changes before save: {_hasUnsavedShuffle}
                """);
            var result = await _saveMutationService.SaveResourceNodeAssignmentsAsync(
                InputSavePath,
                OutputSavePath,
                ResourceNodes.ToArray());

            if (result.Success)
            {
                _hasUnsavedShuffle = false;
                SaveCommand.RaiseCanExecuteChanged();
            }

            AppendLog(result.Success ? "Save completed" : "Save failed", FormatResult(result));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task OpenOutputAsync()
    {
        var outputDirectory = Path.GetDirectoryName(OutputSavePath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return Task.CompletedTask;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = outputDirectory,
            UseShellExecute = true
        });
        AppendLog("Opened output directory", outputDirectory);

        return Task.CompletedTask;
    }

    private Task ResetResourceCountsToDefaultAsync()
    {
        ResetResourceCounts(DefaultCountMap);
        AppendLog("Resource counts reset", $"Reset to defaults: {FormatResourceCounts()}");
        return Task.CompletedTask;
    }

    private Task ResetResourceCountsToInitialAsync()
    {
        if (_initialResourceCounts.Count > 0)
        {
            ResetResourceCounts(_initialResourceCounts);
            AppendLog("Resource counts reset", $"Reset to initial save counts: {FormatResourceCounts()}");
        }

        return Task.CompletedTask;
    }

    private Task IncrementResourceCountAsync(ResourceCountOption? option)
    {
        if (option is not null)
        {
            option.Count += 1;
        }

        return Task.CompletedTask;
    }

    private Task DecrementResourceCountAsync(ResourceCountOption? option)
    {
        if (option is not null && option.Count > 0)
        {
            option.Count -= 1;
        }

        return Task.CompletedTask;
    }

    private Task NodeEditedAsync(ResourceNodeViewModel? node)
    {
        if (node is null)
        {
            return Task.CompletedTask;
        }

        SyncResourceCountsFromNodes();
        SyncPurityDistributionsFromNodes();
        _hasUnsavedShuffle = true;
        SaveCommand.RaiseCanExecuteChanged();
        ShuffleCommand.RaiseCanExecuteChanged();
        AppendLog("Node edited", FormatNodeEdit(node));
        return Task.CompletedTask;
    }

    private Task SetPurityDistributionModeAsync(string? modeName)
    {
        if (Enum.TryParse<PurityDistributionMode>(modeName, ignoreCase: true, out var mode))
        {
            SyncPurityDistributionsFromNodes();
            SelectedPurityDistributionMode = mode;
            AppendLog("Purity mode changed", FormatPuritySettingsSummary());
        }

        return Task.CompletedTask;
    }

    private Task SetEditorTabAsync(string? tabName)
    {
        SelectedEditorTab = tabName switch
        {
            "MapBrush" => "MapBrush",
            "About" => "About",
            _ => "Shuffle"
        };
        AppendLog("Editor tab changed", SelectedEditorTab);

        return Task.CompletedTask;
    }

    private Task ClearLogAsync()
    {
        LogText = string.Empty;
        return Task.CompletedTask;
    }

    private Task CopyLogsAsync()
    {
        if (!string.IsNullOrWhiteSpace(LogText))
        {
            Clipboard.SetText(LogText);
            AppendLog("Logs copied", "Current log text was copied to clipboard.");
        }

        return Task.CompletedTask;
    }

    private Task OpenSupportAsync()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://discord.gg/w8hqmrmn5J",
            UseShellExecute = true
        });
        AppendLog("Support link opened", "https://discord.gg/w8hqmrmn5J");

        return Task.CompletedTask;
    }

    private Task OpenGitHubAsync()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/DrunkenCorsar/SatisfactoryNodeEditor",
            UseShellExecute = true
        });
        AppendLog("GitHub link opened", "https://github.com/DrunkenCorsar/SatisfactoryNodeEditor");

        return Task.CompletedTask;
    }

    private static string GetDisplayVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null
            ? "0.1.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private void ResetResourceCounts(IReadOnlyDictionary<string, int> counts)
    {
        foreach (var option in ResourceCounts)
        {
            option.PropertyChanged -= ResourceCount_PropertyChanged;
        }

        _suppressResourceCountLogging = true;
        ResourceCounts.Clear();
        foreach (var option in CreateDefaultResourceCounts())
        {
            if (counts.TryGetValue(option.DisplayName, out var count))
            {
                option.Count = count;
            }

            option.PropertyChanged += ResourceCount_PropertyChanged;
            ResourceCounts.Add(option);
        }
        _suppressResourceCountLogging = false;
        RememberCurrentResourceCounts();

        RecalculateResourceCounts();
    }

    private void ResourceCount_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResourceCountOption.Count))
        {
            RecalculateResourceCounts();
            if (!_suppressResourceCountLogging && sender is ResourceCountOption option)
            {
                var oldCount = _lastLoggedResourceCounts.TryGetValue(option.DisplayName, out var remembered)
                    ? remembered
                    : option.Count;
                if (oldCount != option.Count)
                {
                    AppendLog("Resource count changed", $"{option.DisplayName}: {oldCount} -> {option.Count}. Total: {CurrentTotalNodes}/{ExpectedTotalNodes}");
                    _lastLoggedResourceCounts[option.DisplayName] = option.Count;
                }
            }
        }
    }

    private void RememberCurrentResourceCounts()
    {
        _lastLoggedResourceCounts.Clear();
        foreach (var option in ResourceCounts)
        {
            _lastLoggedResourceCounts[option.DisplayName] = option.Count;
        }
    }

    private void SyncResourceCountsFromNodes()
    {
        var counts = BuildCountsFromSave(ResourceNodes);
        foreach (var option in ResourceCounts)
        {
            option.Count = counts.TryGetValue(option.DisplayName, out var count) ? count : 0;
        }

        RecalculateResourceCounts();
    }

    private void RecalculateResourceCounts()
    {
        var total = CurrentTotalNodes;
        foreach (var option in ResourceCounts)
        {
            option.Percentage = total == 0 ? 0 : option.Count * 100.0 / total;
        }

        UpdatePurityDistributionNodeCounts(total);
        OnPropertyChanged(nameof(CurrentTotalNodes));
        OnPropertyChanged(nameof(TotalNodesDisplay));
        OnPropertyChanged(nameof(TotalNodesBrush));
        OnPropertyChanged(nameof(IsNodeCountOverMaximum));
        ShuffleCommand.RaiseCanExecuteChanged();
        ShufflePuritiesCommand.RaiseCanExecuteChanged();
    }

    private void UpdatePurityDistributionNodeCounts(int total)
    {
        GlobalPurityDistribution.TotalNodeCount = total;
        foreach (var distribution in PerResourcePurityDistributions)
        {
            distribution.TotalNodeCount = ResourceCounts.FirstOrDefault(option =>
                option.DisplayName.Equals(distribution.ResourceName, StringComparison.OrdinalIgnoreCase))?.Count ?? 0;
        }
    }

    private PurityShuffleSettings BuildPurityShuffleSettings() => new(
        SelectedPurityDistributionMode,
        ToDistribution(GlobalPurityDistribution),
        PerResourcePurityDistributions.ToDictionary(
            distribution => distribution.ResourceName,
            ToDistribution,
            StringComparer.OrdinalIgnoreCase));

    private static PurityDistribution ToDistribution(PurityDistributionViewModel distribution) => new(
        distribution.ImpurePercent,
        distribution.NormalPercent,
        distribution.PurePercent);

    private void SyncPurityDistributionsFromNodes()
    {
        var ordinaryNodes = ResourceNodes
            .Where(IsOrdinaryNonEmptyResourceNode)
            .ToArray();

        ApplyDistributionFromNodes(GlobalPurityDistribution, ordinaryNodes);
        foreach (var distribution in PerResourcePurityDistributions)
        {
            var resourceNodes = ordinaryNodes
                .Where(node => NormalizeResourceName(node.ResourceType).Equals(distribution.ResourceName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            ApplyDistributionFromNodes(distribution, resourceNodes);
        }

        RecalculateResourceCounts();
    }

    private static bool IsOrdinaryNonEmptyResourceNode(ResourceNodeViewModel node) =>
        node.NodeKind.Equals("ResourceNode", StringComparison.OrdinalIgnoreCase) &&
        !node.ResourceType.Equals("Empty", StringComparison.OrdinalIgnoreCase);

    private static void ApplyDistributionFromNodes(PurityDistributionViewModel distribution, IReadOnlyCollection<ResourceNodeViewModel> nodes)
    {
        distribution.TotalNodeCount = nodes.Count;
        if (nodes.Count == 0)
        {
            distribution.SetPercentages(25, 50, 25);
            return;
        }

        var impure = nodes.Count(node => PurityKey(node.Purity) == "Impure");
        var normal = nodes.Count(node => PurityKey(node.Purity) == "Normal");
        var pure = nodes.Count(node => PurityKey(node.Purity) == "Pure");
        distribution.SetPercentages(
            impure * 100.0 / nodes.Count,
            normal * 100.0 / nodes.Count,
            pure * 100.0 / nodes.Count);
    }

    private void InitializePerResourcePurityDistributions()
    {
        PerResourcePurityDistributions.Clear();
        foreach (var option in CreateDefaultResourceCounts())
        {
            PerResourcePurityDistributions.Add(new ResourcePurityDistributionViewModel
            {
                ResourceName = option.DisplayName,
                ResourceBrush = ToBrush(option.ColorHex),
                TotalNodeCount = option.Count
            });
        }
    }

    private string BuildOutputPath(string inputSavePath)
    {
        var directory = IsBundledTemplatePath(inputSavePath)
            ? _saveFileDialogService.GetPreferredSaveDirectory() ?? Path.GetDirectoryName(inputSavePath) ?? string.Empty
            : Path.GetDirectoryName(inputSavePath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(inputSavePath);
        return Path.Combine(directory, $"{name}_NODE_EDITOR.sav");
    }

    private static bool IsBundledTemplatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedPath = Path.GetFullPath(path);
        var templateDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Assets", "Templates"));
        return normalizedPath.StartsWith(templateDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static int DefaultTotalNodes => DefaultCountMap.Values.Sum();

    private static IReadOnlyDictionary<string, int> DefaultCountMap { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Iron"] = 127,
        ["Limestone"] = 94,
        ["Coal"] = 62,
        ["Copper"] = 55,
        ["Crude Oil"] = 30,
        ["SAM"] = 19,
        ["Caterium"] = 17,
        ["Bauxite"] = 17,
        ["Raw Quartz"] = 17,
        ["Sulfur"] = 16,
        ["Uranium"] = 5
    };

    private static ResourceCountOption[] CreateDefaultResourceCounts() =>
    [
        new() { ResourceKey = "iron", DisplayName = "Iron", Count = 127, DefaultCount = 127, IconPath = "/Assets/Resources/iron.png", ColorHex = "#9AA0A6" },
        new() { ResourceKey = "limestone", DisplayName = "Limestone", Count = 94, DefaultCount = 94, IconPath = "/Assets/Resources/limestone.png", ColorHex = "#D9D1B3" },
        new() { ResourceKey = "coal", DisplayName = "Coal", Count = 62, DefaultCount = 62, IconPath = "/Assets/Resources/coal.png", ColorHex = "#343A40" },
        new() { ResourceKey = "copper", DisplayName = "Copper", Count = 55, DefaultCount = 55, IconPath = "/Assets/Resources/copper.png", ColorHex = "#C87533" },
        new() { ResourceKey = "crude-oil", DisplayName = "Crude Oil", Count = 30, DefaultCount = 30, IconPath = "/Assets/Resources/other.png", ColorHex = "#064B76" },
        new() { ResourceKey = "sam", DisplayName = "SAM", Count = 19, DefaultCount = 19, IconPath = "/Assets/Resources/other.png", ColorHex = "#9B63D8" },
        new() { ResourceKey = "caterium", DisplayName = "Caterium", Count = 17, DefaultCount = 17, IconPath = "/Assets/Resources/caterium.png", ColorHex = "#D5A72D" },
        new() { ResourceKey = "bauxite", DisplayName = "Bauxite", Count = 17, DefaultCount = 17, IconPath = "/Assets/Resources/bauxite.png", ColorHex = "#DC5B2E" },
        new() { ResourceKey = "raw-quartz", DisplayName = "Raw Quartz", Count = 17, DefaultCount = 17, IconPath = "/Assets/Resources/quartz.png", ColorHex = "#D8FBFF" },
        new() { ResourceKey = "sulfur", DisplayName = "Sulfur", Count = 16, DefaultCount = 16, IconPath = "/Assets/Resources/sulfur.png", ColorHex = "#F3DB3F" },
        new() { ResourceKey = "uranium", DisplayName = "Uranium", Count = 5, DefaultCount = 5, IconPath = "/Assets/Resources/uranium.png", ColorHex = "#49B75C" }
    ];

    private static Dictionary<string, int> BuildCountsFromSave(IEnumerable<ResourceNodeViewModel> nodes)
    {
        var counts = CreateDefaultResourceCounts().ToDictionary(option => option.DisplayName, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var group in nodes.Where(node => node.NodeKind.Equals("ResourceNode", StringComparison.OrdinalIgnoreCase))
            .GroupBy(node => NormalizeResourceName(node.ResourceType), StringComparer.OrdinalIgnoreCase))
        {
            if (counts.ContainsKey(group.Key))
            {
                counts[group.Key] = group.Count();
            }
        }

        return counts;
    }

    private static string NormalizeResourceName(string resourceType) => resourceType.Trim().ToLowerInvariant() switch
    {
        "quartz" => "Raw Quartz",
        "raw quartz" => "Raw Quartz",
        "iron ore" => "Iron",
        "copper ore" => "Copper",
        "caterium ore" => "Caterium",
        "uranium ore" => "Uranium",
        _ => resourceType.Trim()
    };

    private static string PurityKey(string purity) => purity.Trim().ToLowerInvariant() switch
    {
        "impure" or "inpure" or "rp_inpure" => "Impure",
        "normal" or "rp_normal" => "Normal",
        "pure" or "rp_pure" => "Pure",
        _ => "Unknown"
    };

    private static bool CountsMatch(IReadOnlyDictionary<string, int> first, IReadOnlyDictionary<string, int> second) =>
        first.Count == second.Count && first.All(pair => second.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static Brush GetModeButtonBrush(bool isSelected) =>
        isSelected ? ToBrush("#6f4fd8") : ToBrush("#1f2933");

    private static Brush GetModeTextBrush(bool isSelected) =>
        isSelected ? Brushes.White : ToBrush("#d5dae3");

    private static Brush GetTabButtonBrush(bool isSelected) =>
        isSelected ? ToBrush("#273241") : ToBrush("#111820");

    private static Brush GetTabTextBrush(bool isSelected) =>
        isSelected ? ToBrush("#ffffff") : ToBrush("#9aa6b2");

    private static Brush ToBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private void AppendLog(string title, string? details = null)
    {
        var entry = new StringBuilder();
        entry.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {title}");
        if (!string.IsNullOrWhiteSpace(details))
        {
            entry.AppendLine(details.Trim());
        }

        var current = LogText.TrimEnd();
        LogText = string.IsNullOrWhiteSpace(current)
            ? entry.ToString().TrimEnd()
            : $"{current}{Environment.NewLine}{Environment.NewLine}{entry.ToString().TrimEnd()}";
    }

    private string FormatResourceCounts() =>
        string.Join(", ", ResourceCounts.Select(option => $"{option.DisplayName}={option.Count}"));

    private string FormatPuritySettingsSummary()
    {
        var builder = new StringBuilder();
        builder.Append($"Mode={SelectedPurityDistributionMode}; Global={FormatDistribution(GlobalPurityDistribution)}");
        if (SelectedPurityDistributionMode == PurityDistributionMode.PerResource)
        {
            builder.Append("; PerResource=");
            builder.Append(string.Join(", ", PerResourcePurityDistributions.Select(distribution =>
                $"{distribution.ResourceName}({FormatDistribution(distribution)})")));
        }

        return builder.ToString();
    }

    private static string FormatDistribution(PurityDistributionViewModel distribution) =>
        $"Impure {distribution.ImpurePercent:0}%/{distribution.ImpureNodeCount}, Normal {distribution.NormalPercent:0}%/{distribution.NormalNodeCount}, Pure {distribution.PurePercent:0}%/{distribution.PureNodeCount}";

    private static string FormatNodeEdit(ResourceNodeViewModel node)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Id: {node.Id}");
        builder.AppendLine($"Type: {node.NodeKind}");
        builder.AppendLine($"Resource: {node.ResourceType}");
        builder.AppendLine($"Purity: {node.Purity}");
        builder.AppendLine($"World: X {node.WorldX:0.##}, Y {node.WorldY:0.##}, Z {node.WorldZ:0.##}");
        if (node.Satellites.Count > 0)
        {
            builder.AppendLine("Satellites:");
            foreach (var satellite in node.Satellites)
            {
                builder.AppendLine($"- {satellite.DisplayName}: {satellite.Id} / {satellite.Purity}");
            }
        }

        return builder.ToString().Trim();
    }

    private static string FormatResult(SaveMutationResult result)
    {
        var status = result.Success ? "Mutation completed." : "Mutation failed.";
        return $"""
            {status}

            Candidate nodes found: {result.CandidateNodesFound}
            Nodes changed: {result.NodesChanged}
            Output save: {result.OutputSavePath}
            Backup save: {FormatBackupPath(result.BackupSavePath)}

            {result.Log}
            {result.ErrorMessage}
            """.Trim();
    }

    private static string FormatBackupPath(string backupPath) =>
        string.IsNullOrWhiteSpace(backupPath) ? "Not created" : backupPath;

    private static bool HasAcceptedSafetyNotice()
    {
        try
        {
            return File.Exists(GetSafetyNoticeAcceptedPath());
        }
        catch
        {
            return false;
        }
    }

    private static string GetSafetyNoticeAcceptedPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SatisfactoryNodeEditor",
        "settings",
        "safety-notice-0.1.0.accepted");

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
