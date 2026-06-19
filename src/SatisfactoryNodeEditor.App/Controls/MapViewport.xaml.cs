using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SatisfactoryNodeEditor.App.Services;
using SatisfactoryNodeEditor.App.ViewModels;
using ShapeEllipse = System.Windows.Shapes.Ellipse;
using ShapePath = System.Windows.Shapes.Path;
using ShapeRectangle = System.Windows.Shapes.Rectangle;

namespace SatisfactoryNodeEditor.App.Controls;

public partial class MapViewport : UserControl
{
    public static readonly DependencyProperty NodesProperty = DependencyProperty.Register(
        nameof(Nodes),
        typeof(IEnumerable),
        typeof(MapViewport),
        new PropertyMetadata(null, OnMapDataChanged));

    public static readonly DependencyProperty NodeEditedCommandProperty = DependencyProperty.Register(
        nameof(NodeEditedCommand),
        typeof(ICommand),
        typeof(MapViewport),
        new PropertyMetadata(null));

    public static readonly DependencyProperty IsBrushResourceChangeEnabledProperty = DependencyProperty.Register(
        nameof(IsBrushResourceChangeEnabled),
        typeof(bool),
        typeof(MapViewport),
        new PropertyMetadata(false));

    public static readonly DependencyProperty BrushResourceTypeProperty = DependencyProperty.Register(
        nameof(BrushResourceType),
        typeof(string),
        typeof(MapViewport),
        new FrameworkPropertyMetadata("Iron", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IsBrushPurityChangeEnabledProperty = DependencyProperty.Register(
        nameof(IsBrushPurityChangeEnabled),
        typeof(bool),
        typeof(MapViewport),
        new PropertyMetadata(false));

    public static readonly DependencyProperty BrushPurityProperty = DependencyProperty.Register(
        nameof(BrushPurity),
        typeof(string),
        typeof(MapViewport),
        new FrameworkPropertyMetadata("Normal", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty BrushSizePixelsProperty = DependencyProperty.Register(
        nameof(BrushSizePixels),
        typeof(double),
        typeof(MapViewport),
        new PropertyMetadata(32.0, OnBrushSizeChanged));

    private const double ResourceNodeSize = 14;
    private const double WellNodeSize = 18;
    private const double GeyserNodeSize = 18;
    private const double MinZoom = 0.08;
    private const double MaxZoom = 8;
    private readonly ResourceNodeStyleProvider _styleProvider = new();
    private readonly List<FrameworkElement> _markerVisuals = [];
    private readonly Dictionary<string, ShapePath> _resourceMarkerVisuals = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hiddenResourceTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hiddenPurities = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hiddenNodeCategories = new(StringComparer.OrdinalIgnoreCase);
    private FrameworkElement? _selectedNodeCard;
    private INotifyCollectionChanged? _notifiedNodes;
    private bool _isLegendCollapsed;
    private bool _isEqualizerCollapsed = true;
    private bool _isDragging;
    private bool _isBrushPainting;
    private bool _isBrushErasing;
    private Point _lastDragPoint;
    private Point _lastMousePoint;
    private Point _pendingNodeClickPoint;
    private ResourceNodeViewModel? _pendingNodeClick;
    private bool _pendingNodeClickMoved;
    private readonly HashSet<ResourceNodeViewModel> _brushEditedNodes = [];

    public MapViewport()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            InputManager.Current.PreProcessInput += InputManager_PreProcessInput;
            RenderLegend();
            UpdateLegendCollapseState();
            UpdateEqualizerCollapseState();
            RenderMap();
            Dispatcher.BeginInvoke(new Action(FitMapToViewport), System.Windows.Threading.DispatcherPriority.ContextIdle);
        };
        Unloaded += (_, _) => InputManager.Current.PreProcessInput -= InputManager_PreProcessInput;
        SizeChanged += (_, _) =>
        {
            if (Nodes is null)
            {
                FitMapToViewport();
            }
        };
    }

    public IEnumerable? Nodes
    {
        get => (IEnumerable?)GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    public ICommand? NodeEditedCommand
    {
        get => (ICommand?)GetValue(NodeEditedCommandProperty);
        set => SetValue(NodeEditedCommandProperty, value);
    }

    public bool IsBrushResourceChangeEnabled
    {
        get => (bool)GetValue(IsBrushResourceChangeEnabledProperty);
        set => SetValue(IsBrushResourceChangeEnabledProperty, value);
    }

    public string BrushResourceType
    {
        get => (string)GetValue(BrushResourceTypeProperty);
        set => SetValue(BrushResourceTypeProperty, value);
    }

    public bool IsBrushPurityChangeEnabled
    {
        get => (bool)GetValue(IsBrushPurityChangeEnabledProperty);
        set => SetValue(IsBrushPurityChangeEnabledProperty, value);
    }

    public string BrushPurity
    {
        get => (string)GetValue(BrushPurityProperty);
        set => SetValue(BrushPurityProperty, value);
    }

    public double BrushSizePixels
    {
        get => (double)GetValue(BrushSizePixelsProperty);
        set => SetValue(BrushSizePixelsProperty, value);
    }

    private static void OnMapDataChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is MapViewport viewport)
        {
            viewport.UpdateCollectionSubscription(args.NewValue);
            viewport.RenderMap();
        }
    }

    private static void OnBrushSizeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is MapViewport viewport)
        {
            viewport.UpdateBrushVisual(Mouse.GetPosition(viewport));
        }
    }

    private void UpdateCollectionSubscription(object? newValue)
    {
        if (_notifiedNodes is not null)
        {
            _notifiedNodes.CollectionChanged -= Nodes_CollectionChanged;
            _notifiedNodes = null;
        }

        if (newValue is INotifyCollectionChanged notified)
        {
            _notifiedNodes = notified;
            _notifiedNodes.CollectionChanged += Nodes_CollectionChanged;
        }
    }

    private void Nodes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RenderMap();

    private void RenderMap()
    {
        if (MapCanvas is null)
        {
            return;
        }

        _markerVisuals.Clear();
        _resourceMarkerVisuals.Clear();
        _selectedNodeCard = null;
        MapCanvas.Children.Clear();
        DrawMapBackground();

        var nodes = Nodes?.Cast<ResourceNodeViewModel>().ToArray() ?? [];
        foreach (var node in nodes.Where(node => !IsNodeHidden(node)))
        {
            var marker = IsGeyser(node)
                ? CreateGeyserNode(node)
                : IsWell(node)
                    ? CreateWellNode(node)
                    : CreateResourceNode(node);
            _markerVisuals.Add(marker);
            if (CanEditNode(node) && marker is ShapePath markerPath)
            {
                _resourceMarkerVisuals[node.Id] = markerPath;
            }

            MapCanvas.Children.Add(marker);
        }

        ApplyMarkerScreenScale();

        if (nodes.Length == 0)
        {
            DrawSampleMarkers();
            DrawEmptyMessage();
        }
    }

    private void CloseNodeCard()
    {
        if (_selectedNodeCard is null)
        {
            return;
        }

        _markerVisuals.Remove(_selectedNodeCard);
        MapCanvas.Children.Remove(_selectedNodeCard);
        _selectedNodeCard = null;
    }

    private void DrawMapBackground()
    {
        RootBorder.Background = GetBrushResource("AppPanelBrush", SystemColors.ControlBrush);
        var imagePath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Maps", "satisfactory-map.png");
        if (System.IO.File.Exists(imagePath))
        {
            var image = new Image
            {
                Width = SatisfactoryMapCoordinateConverter.MapWidth,
                Height = SatisfactoryMapCoordinateConverter.MapHeight,
                Stretch = Stretch.Fill,
                Source = LoadBitmap(imagePath),
                Opacity = WindowsThemePalette.IsDarkMode ? 1.0 : 0.74
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            RenderOptions.SetEdgeMode(image, EdgeMode.Unspecified);
            image.SnapsToDevicePixels = false;
            MapCanvas.Children.Add(image);
            MapCanvas.Children.Add(new ShapeRectangle
            {
                Width = SatisfactoryMapCoordinateConverter.MapWidth,
                Height = SatisfactoryMapCoordinateConverter.MapHeight,
                Fill = GetBrushResource("AppPanelBrush", SystemColors.ControlBrush),
                Opacity = WindowsThemePalette.IsDarkMode ? 0.08 : 0.28,
                IsHitTestVisible = false
            });
            return;
        }

        MapCanvas.Children.Add(new ShapeRectangle
        {
            Width = SatisfactoryMapCoordinateConverter.MapWidth,
            Height = SatisfactoryMapCoordinateConverter.MapHeight,
            Fill = GetBrushResource("AppPanelAltBrush", SystemColors.ControlLightBrush)
        });
    }

    private void RenderLegend()
    {
        if (LegendPanel is null)
        {
            return;
        }

        LegendPanel.Children.Clear();
        LegendPanel.Children.Add(CreateLegendSection("Purity"));
        LegendPanel.Children.Add(CreateLegendShapeRow("Impure", CreatePurityLegendIcon("Impure"), IsPurityHidden("Impure"), () => TogglePurityVisibility("Impure")));
        LegendPanel.Children.Add(CreateLegendShapeRow("Normal", CreatePurityLegendIcon("Normal"), IsPurityHidden("Normal"), () => TogglePurityVisibility("Normal")));
        LegendPanel.Children.Add(CreateLegendShapeRow("Pure", CreatePurityLegendIcon("Pure"), IsPurityHidden("Pure"), () => TogglePurityVisibility("Pure")));
        LegendPanel.Children.Add(CreateLegendSection("Special nodes"));
        LegendPanel.Children.Add(CreateLegendShapeRow("Fracking well", CreateWellLegendIcon("Crude Oil"), IsNodeCategoryHidden("Well"), () => ToggleNodeCategoryVisibility("Well")));
        LegendPanel.Children.Add(CreateLegendShapeRow("Geyser", CreateGeyserLegendIcon(), IsNodeCategoryHidden("Geyser"), () => ToggleNodeCategoryVisibility("Geyser")));
        LegendPanel.Children.Add(CreateLegendShapeRow("Empty / removed", CreateEmptyLegendIcon(), IsNodeCategoryHidden("Empty"), () => ToggleNodeCategoryVisibility("Empty")));
        LegendPanel.Children.Add(CreateLegendSection("Resources"));

        var resourceGrid = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(0, 2, 0, 0)
        };

        foreach (var resource in _styleProvider.GetLegendResourceTypes())
        {
            resourceGrid.Children.Add(CreateResourceLegendRow(resource));
        }

        LegendPanel.Children.Add(resourceGrid);
        UpdateLegendCollapseState();
    }

    private void LegendHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        PrepareOverlayWidthForToggle(LegendBorder, _isLegendCollapsed);
        _isLegendCollapsed = !_isLegendCollapsed;
        UpdateLegendCollapseState();
        e.Handled = true;
    }

    private void EqualizerHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        PrepareOverlayWidthForToggle(EqualizerBorder, _isEqualizerCollapsed);
        _isEqualizerCollapsed = !_isEqualizerCollapsed;
        UpdateEqualizerCollapseState();
        e.Handled = true;
    }

    private void UpdateLegendCollapseState()
    {
        UpdateOverlayCollapseState(LegendPanel, LegendToggleIcon, _isLegendCollapsed);
    }

    private void UpdateEqualizerCollapseState()
    {
        UpdateOverlayCollapseState(EqualizerPanel, EqualizerToggleIcon, _isEqualizerCollapsed);
    }

    private static void UpdateOverlayCollapseState(FrameworkElement? content, ShapePath? icon, bool isCollapsed)
    {
        if (content is not null)
        {
            content.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        }

        if (icon is not null)
        {
            icon.Data = Geometry.Parse(isCollapsed ? "M 2,8 L 6,4 L 10,8" : "M 2,4 L 6,8 L 10,4");
        }
    }

    private static void PrepareOverlayWidthForToggle(FrameworkElement? border, bool isCurrentlyCollapsed)
    {
        if (border is null)
        {
            return;
        }

        if (isCurrentlyCollapsed)
        {
            border.Width = double.NaN;
            return;
        }

        if (border.ActualWidth > 0)
        {
            border.Width = border.ActualWidth;
        }
    }

    private FrameworkElement CreateLegendSection(string title)
    {
        var block = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 4)
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedTextBrush");
        return block;
    }

    private FrameworkElement CreateLegendShapeRow(string label, FrameworkElement icon, bool isHidden, Action toggle)
    {
        icon.Opacity = isHidden ? 0.32 : 1;
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 2),
            Cursor = Cursors.Hand
        };
        row.Children.Add(icon);
        row.Children.Add(CreateLegendLabel(label, isHidden));
        row.MouseLeftButtonDown += (_, args) =>
        {
            toggle();
            args.Handled = true;
        };
        return row;
    }

    private FrameworkElement CreateResourceLegendRow(string resource)
    {
        var isHidden = _hiddenResourceTypes.Contains(resource);
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 10, 2),
            Cursor = Cursors.Hand
        };
        row.Children.Add(new ShapeEllipse
        {
            Width = 10,
            Height = 10,
            Fill = _styleProvider.GetResourceBrush(resource),
            Stroke = GetBrushResource("AppBorderBrush", SystemColors.ActiveBorderBrush),
            StrokeThickness = 0.8,
            Margin = new Thickness(0, 3, 7, 0),
            Opacity = isHidden ? 0.32 : 1
        });
        row.Children.Add(CreateLegendLabel(resource, isHidden));
        row.MouseLeftButtonDown += (_, args) =>
        {
            ToggleResourceVisibility(resource);
            args.Handled = true;
        };
        return row;
    }

    private FrameworkElement CreateLegendLabel(string label, bool isHidden = false)
    {
        var block = new TextBlock
        {
            Text = label,
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
            TextDecorations = isHidden ? TextDecorations.Strikethrough : null,
            Opacity = isHidden ? 0.5 : 1
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, isHidden ? "AppMutedTextBrush" : "AppTextBrush");
        return block;
    }

    private void ToggleResourceVisibility(string resource)
    {
        if (!_hiddenResourceTypes.Add(resource))
        {
            _hiddenResourceTypes.Remove(resource);
        }

        RefreshVisibilityFilters();
    }

    private void TogglePurityVisibility(string purity)
    {
        var normalizedPurity = NormalizeEditablePurity(purity);
        if (!_hiddenPurities.Add(normalizedPurity))
        {
            _hiddenPurities.Remove(normalizedPurity);
        }

        RefreshVisibilityFilters();
    }

    private void ToggleNodeCategoryVisibility(string category)
    {
        if (!_hiddenNodeCategories.Add(category))
        {
            _hiddenNodeCategories.Remove(category);
        }

        RefreshVisibilityFilters();
    }

    private void RefreshVisibilityFilters()
    {
        if (_selectedNodeCard is not null)
        {
            CloseNodeCard();
        }

        RenderLegend();
        RenderMap();
    }

    private FrameworkElement CreatePurityLegendIcon(string purity) => new ShapePath
    {
        Width = 16,
        Height = 16,
        Data = _styleProvider.GetSimpleShapeGeometry(purity, 16),
        Fill = GetBrushResource("AppMutedTextBrush", SystemColors.GrayTextBrush),
        Stroke = GetBrushResource("AppTextBrush", SystemColors.WindowTextBrush),
        StrokeThickness = 1.2,
        Margin = new Thickness(0, 1, 8, 0)
    };

    private FrameworkElement CreateWellLegendIcon(string resourceType)
    {
        const double size = 16;

        return new ShapePath
        {
            Width = size,
            Height = size,
            Data = CreateWellGeometry(size),
            Fill = _styleProvider.GetResourceBrush(resourceType),
            Stroke = GetBrushResource("AppTextBrush", SystemColors.WindowTextBrush),
            StrokeThickness = 1.2,
            Margin = new Thickness(0, 1, 8, 0)
        };
    }

    private FrameworkElement CreateGeyserLegendIcon()
    {
        const double size = 16;
        const double half = size / 2;
        return new ShapePath
        {
            Width = size,
            Height = size,
            Data = Geometry.Parse(FormattableString.Invariant(
                $"M {half},0 L {size * 0.62},{size * 0.36} L {size},{half} L {size * 0.62},{size * 0.64} L {half},{size} L {size * 0.38},{size * 0.64} L 0,{half} L {size * 0.38},{size * 0.36} Z")),
            Fill = _styleProvider.GetResourceBrush("Geothermal"),
            Stroke = GetBrushResource("AppTextBrush", SystemColors.WindowTextBrush),
            StrokeThickness = 1.2,
            Margin = new Thickness(0, 1, 8, 0)
        };
    }

    private FrameworkElement CreateEmptyLegendIcon() => new ShapePath
    {
        Width = 16,
        Height = 16,
        Data = Geometry.Parse("M 0,0 L 16,16 M 16,0 L 0,16"),
        Fill = Brushes.Transparent,
        Stroke = GetBrushResource("AppTextBrush", SystemColors.WindowTextBrush),
        StrokeThickness = 2,
        Margin = new Thickness(0, 1, 8, 0)
    };

    private static BitmapImage LoadBitmap(string imagePath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private FrameworkElement CreateResourceNode(ResourceNodeViewModel node)
    {
        var icon = new ShapePath
        {
            Width = ResourceNodeSize,
            Height = ResourceNodeSize,
            Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        UpdateResourceNodeVisual(icon, node);

        icon.MouseLeftButtonDown += (_, args) => BeginNodeClick(node, args);
        icon.MouseLeftButtonUp += (_, args) => CompleteNodeClick(node, args);
        icon.MouseRightButtonDown += (_, args) => TryStartBrushPaint(args);
        icon.MouseDown += (_, args) => PickBrushFromNode(node, args);
        Canvas.SetLeft(icon, node.MapX - ResourceNodeSize / 2);
        Canvas.SetTop(icon, node.MapY - ResourceNodeSize / 2);
        return icon;
    }

    private void UpdateResourceNodeVisual(ShapePath icon, ResourceNodeViewModel node)
    {
        var isEmpty = node.ResourceType.Equals("Empty", StringComparison.OrdinalIgnoreCase);
        icon.Data = isEmpty
            ? Geometry.Parse(FormattableString.Invariant($"M 0,0 L {ResourceNodeSize},{ResourceNodeSize} M {ResourceNodeSize},0 L 0,{ResourceNodeSize}"))
            : _styleProvider.GetSimpleShapeGeometry(node.Purity, ResourceNodeSize);
        icon.Fill = isEmpty ? Brushes.Transparent : _styleProvider.GetResourceBrush(node.ResourceType);
        icon.Stroke = Brushes.Black;
        icon.StrokeThickness = isEmpty ? 2.4 : 1.4;
    }

    private FrameworkElement CreateWellNode(ResourceNodeViewModel node)
    {
        var icon = new ShapePath
        {
            Width = WellNodeSize,
            Height = WellNodeSize,
            Data = CreateWellGeometry(WellNodeSize),
            Fill = _styleProvider.GetResourceBrush(node.ResourceType),
            Stroke = Brushes.White,
            StrokeThickness = 1.6,
            Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        icon.MouseLeftButtonDown += (_, args) => BeginNodeClick(node, args);
        icon.MouseLeftButtonUp += (_, args) => CompleteNodeClick(node, args);
        icon.MouseRightButtonDown += (_, args) => TryStartBrushPaint(args);
        icon.MouseDown += (_, args) => PickBrushFromNode(node, args);
        Canvas.SetLeft(icon, node.MapX - WellNodeSize / 2);
        Canvas.SetTop(icon, node.MapY - WellNodeSize / 2);
        return icon;
    }

    private static Geometry CreateWellGeometry(double size)
    {
        var group = new GeometryGroup();
        group.FillRule = FillRule.Nonzero;
        var half = size / 2;
        group.Children.Add(Geometry.Parse(FormattableString.Invariant(
            $"M {half},{size * 0.05} L {size * 0.86},{size * 0.28} L {size * 0.86},{size * 0.72} L {half},{size * 0.95} L {size * 0.14},{size * 0.72} L {size * 0.14},{size * 0.28} Z")));
        group.Children.Add(new EllipseGeometry(new Point(half, half), size * 0.18, size * 0.18));
        group.Children.Add(Geometry.Parse(FormattableString.Invariant(
            $"M {half},0 L {half},{size * 0.22} M {half},{size * 0.78} L {half},{size} M 0,{half} L {size * 0.22},{half} M {size * 0.78},{half} L {size},{half}")));
        return group;
    }

    private FrameworkElement CreateGeyserNode(ResourceNodeViewModel node)
    {
        var half = GeyserNodeSize / 2;
        var geometry = Geometry.Parse(FormattableString.Invariant(
            $"M {half},0 L {GeyserNodeSize * 0.62},{GeyserNodeSize * 0.36} L {GeyserNodeSize},{half} L {GeyserNodeSize * 0.62},{GeyserNodeSize * 0.64} L {half},{GeyserNodeSize} L {GeyserNodeSize * 0.38},{GeyserNodeSize * 0.64} L 0,{half} L {GeyserNodeSize * 0.38},{GeyserNodeSize * 0.36} Z"));

        var icon = new ShapePath
        {
            Width = GeyserNodeSize,
            Height = GeyserNodeSize,
            Data = geometry,
            Fill = new SolidColorBrush(Color.FromRgb(86, 224, 198)),
            Stroke = Brushes.Black,
            StrokeThickness = 1.5,
            Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        icon.MouseLeftButtonDown += (_, args) => BeginNodeClick(node, args);
        icon.MouseLeftButtonUp += (_, args) => CompleteNodeClick(node, args);
        icon.MouseRightButtonDown += (_, args) => TryStartBrushPaint(args);
        icon.MouseDown += (_, args) => PickBrushFromNode(node, args);
        Canvas.SetLeft(icon, node.MapX - GeyserNodeSize / 2);
        Canvas.SetTop(icon, node.MapY - GeyserNodeSize / 2);
        return icon;
    }

    private void DrawEmptyMessage()
    {
        var message = new TextBlock
        {
            Text = "Load a .sav file to inspect and shuffle resource nodes",
            Foreground = GetBrushResource("AppTextBrush", SystemColors.ControlTextBrush),
            FontSize = 104,
            FontWeight = FontWeights.SemiBold
        };

        Canvas.SetLeft(message, 520);
        Canvas.SetTop(message, 2380);
        MapCanvas.Children.Add(message);
    }

    private void DrawSampleMarkers()
    {
        var samples = new[]
        {
            CreateSampleNode("Iron Ore", "Pure", 1420, 1660),
            CreateSampleNode("Iron Ore", "Normal", 1560, 1830),
            CreateSampleNode("Copper Ore", "Normal", 2060, 1470),
            CreateSampleNode("Copper Ore", "Impure", 2230, 1660),
            CreateSampleNode("Limestone", "Pure", 2460, 2120),
            CreateSampleNode("Limestone", "Normal", 2740, 2310),
            CreateSampleNode("Coal", "Normal", 3180, 1810),
            CreateSampleNode("Coal", "Pure", 3420, 2040),
            CreateSampleNode("Caterium Ore", "Impure", 1820, 2480),
            CreateSampleNode("Quartz", "Normal", 2180, 2840),
            CreateSampleNode("Sulfur", "Pure", 2840, 2920),
            CreateSampleNode("Bauxite", "Impure", 3360, 2680),
            CreateSampleNode("Uranium Ore", "Normal", 3720, 3180),
            CreateSampleNode("Bauxite", "Pure", 3040, 3380),
            CreateSampleNode("Sulfur", "Normal", 1660, 3240),
            CreateSampleNode("Quartz", "Pure", 1120, 2880)
        };

        foreach (var sample in samples)
        {
            if (IsNodeHidden(sample))
            {
                continue;
            }

            var marker = CreateResourceNode(sample);
            _markerVisuals.Add(marker);
            MapCanvas.Children.Add(marker);
        }
    }

    private static ResourceNodeViewModel CreateSampleNode(string resourceType, string purity, double mapX, double mapY) => new()
    {
        Id = $"Preview {resourceType} {purity}",
        NodeKind = "ResourceNode",
        ResourceType = resourceType,
        Purity = purity,
        MapX = mapX,
        MapY = mapY,
        WorldX = mapX,
        WorldY = mapY,
        WorldZ = 0
    };

    private static bool IsWell(ResourceNodeViewModel node) => node.NodeKind.Equals("Well", StringComparison.OrdinalIgnoreCase);

    private static bool IsGeyser(ResourceNodeViewModel node) => node.NodeKind.Equals("Geyser", StringComparison.OrdinalIgnoreCase);

    private static bool CanEditNode(ResourceNodeViewModel node) =>
        node.NodeKind.Equals("ResourceNode", StringComparison.OrdinalIgnoreCase);

    private static bool CanEditWell(ResourceNodeViewModel node) =>
        node.NodeKind.Equals("Well", StringComparison.OrdinalIgnoreCase);

    private static bool IsEmptyResource(string value) =>
        value.Equals("Empty", StringComparison.OrdinalIgnoreCase);

    private bool IsResourceHidden(string resourceType) =>
        _hiddenResourceTypes.Contains(NormalizeEditableResource(resourceType));

    private bool IsPurityHidden(string purity) =>
        _hiddenPurities.Contains(NormalizeEditablePurity(purity));

    private bool IsNodeCategoryHidden(string category) =>
        _hiddenNodeCategories.Contains(category);

    private bool IsNodeHidden(ResourceNodeViewModel node)
    {
        if (IsWell(node))
        {
            return IsNodeCategoryHidden("Well") || IsResourceHidden(node.ResourceType);
        }

        if (IsGeyser(node))
        {
            return IsNodeCategoryHidden("Geyser");
        }

        if (IsEmptyResource(node.ResourceType))
        {
            return IsNodeCategoryHidden("Empty") || IsResourceHidden(node.ResourceType);
        }

        return IsResourceHidden(node.ResourceType) || IsPurityHidden(node.Purity);
    }

    private static string NormalizeEditableResource(string resourceType) => resourceType.Trim().ToLowerInvariant() switch
    {
        "quartz" => "Raw Quartz",
        "iron ore" => "Iron",
        "copper ore" => "Copper",
        "caterium ore" => "Caterium",
        "uranium ore" => "Uranium",
        "" => "Empty",
        _ => resourceType.Trim()
    };

    private static string NormalizeEditablePurity(string purity) => purity.Trim().ToLowerInvariant() switch
    {
        "rp_inpure" or "inpure" => "Impure",
        "rp_normal" => "Normal",
        "rp_pure" => "Pure",
        _ => purity.Equals("Impure", StringComparison.OrdinalIgnoreCase) ||
             purity.Equals("Pure", StringComparison.OrdinalIgnoreCase)
            ? purity
            : "Normal"
    };

    private void BeginNodeClick(ResourceNodeViewModel node, MouseButtonEventArgs args)
    {
        if (TryStartBrushPaint(args))
        {
            return;
        }

        _pendingNodeClick = node;
        _pendingNodeClickPoint = args.GetPosition(this);
        _pendingNodeClickMoved = false;
        _isDragging = true;
        _lastDragPoint = _pendingNodeClickPoint;
        MapCanvas.CaptureMouse();
        Cursor = Cursors.SizeAll;
        args.Handled = true;
    }

    private void CompleteNodeClick(ResourceNodeViewModel node, MouseButtonEventArgs args)
    {
        if (_pendingNodeClick != node)
        {
            return;
        }

        var releasePoint = args.GetPosition(this);
        CompletePendingNodeClick(releasePoint);
        args.Handled = true;
    }

    private bool CompletePendingNodeClick(Point releasePoint)
    {
        var node = _pendingNodeClick;
        if (node is null)
        {
            return false;
        }

        var movement = releasePoint - _pendingNodeClickPoint;
        var exceededDragThreshold =
            Math.Abs(movement.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(movement.Y) > SystemParameters.MinimumVerticalDragDistance;

        if (!_pendingNodeClickMoved && !exceededDragThreshold)
        {
            ShowNodeCard(node);
        }

        _isDragging = false;
        MapCanvas.ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
        UpdateBrushVisual(releasePoint);
        ClearPendingNodeClick();
        return true;
    }

    private void ClearPendingNodeClick()
    {
        _pendingNodeClick = null;
        _pendingNodeClickMoved = false;
    }

    private void ShowNodeCard(ResourceNodeViewModel node)
    {
        if (_selectedNodeCard is not null)
        {
            CloseNodeCard();
        }

        _selectedNodeCard = CreateNodeCard(node);
        _markerVisuals.Add(_selectedNodeCard);
        MapCanvas.Children.Add(_selectedNodeCard);
        Panel.SetZIndex(_selectedNodeCard, 1000);
        ApplyMarkerScreenScale();
    }

    private FrameworkElement CreateNodeCard(ResourceNodeViewModel node)
    {
        var card = new Border
        {
            Width = 280,
            Background = GetBrushResource("AppInputBrush", SystemColors.WindowBrush),
            BorderBrush = GetBrushResource("AppBorderBrush", SystemColors.ActiveBorderBrush),
            BorderThickness = new Thickness(1.4),
            CornerRadius = new CornerRadius(6),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 4,
                Opacity = 0.35
            },
            RenderTransformOrigin = new Point(0, 0)
        };

        var root = new StackPanel();
        root.Children.Add(new Border
        {
            Background = GetCardHeaderBrush(node),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Padding = new Thickness(12, 8, 12, 8),
            Child = CreateCardTitleContent(node)
        });

        var body = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };
        body.Children.Add(CreateInfoLine("Name", node.Id));
        body.Children.Add(CreateInfoLine("Type", node.NodeKind));
        if (CanEditNode(node))
        {
            body.Children.Add(CreateResourceEditorLine(node));
            body.Children.Add(CreatePurityEditorLine(node));
        }
        else if (CanEditWell(node))
        {
            body.Children.Add(CreateWellResourceEditorLine(node));
            body.Children.Add(CreateInfoLine("Purity", "Satellite-specific"));
            body.Children.Add(CreateSatellitePurityEditor(node));
        }
        else
        {
            body.Children.Add(CreateInfoLine("Resource", DisplayValue(node.ResourceType)));
            body.Children.Add(CreateInfoLine("Purity", DisplayValue(node.Purity)));
        }

        body.Children.Add(CreateInfoLine("World X", node.WorldX.ToString("0.##")));
        body.Children.Add(CreateInfoLine("World Y", node.WorldY.ToString("0.##")));
        body.Children.Add(CreateInfoLine("World Z", node.WorldZ.ToString("0.##")));
        root.Children.Add(body);
        card.Child = root;

        PositionNodeCard(card, node);
        return card;
    }

    private Brush GetCardHeaderBrush(ResourceNodeViewModel node)
    {
        if (IsGeyser(node))
        {
            return _styleProvider.GetResourceBrush("Geothermal");
        }

        return _styleProvider.GetResourceBrush(node.ResourceType);
    }

    private FrameworkElement CreateCardTitleContent(ResourceNodeViewModel node)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        var headerTextBrush = GetReadableTextBrush(GetCardHeaderBrush(node));

        if (CanEditNode(node) && !IsEmptyResource(node.ResourceType))
        {
            row.Children.Add(new ShapeEllipse
            {
                Width = 12,
                Height = 12,
                Fill = _styleProvider.GetPurityBrush(node.Purity),
                Stroke = headerTextBrush,
                StrokeThickness = 1,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        row.Children.Add(new TextBlock
        {
            Text = GetCardTitle(node),
            Foreground = headerTextBrush,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        return row;
    }

    private static Brush GetReadableTextBrush(Brush background)
    {
        if (background is not SolidColorBrush solidColorBrush)
        {
            return Brushes.White;
        }

        var color = solidColorBrush.Color;
        var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255;
        return luminance > 0.58 ? Brushes.Black : Brushes.White;
    }

    private void PositionNodeCard(FrameworkElement card, ResourceNodeViewModel node)
    {
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var cardWidth = card.DesiredSize.Width > 0 ? card.DesiredSize.Width : 280;
        var cardHeight = card.DesiredSize.Height > 0 ? card.DesiredSize.Height : 180;
        var scale = Math.Max(0.0001, MapScaleTransform.ScaleX);
        var margin = 12 / scale;
        var cardWidthInMap = cardWidth / scale;
        var cardHeightInMap = cardHeight / scale;
        var visibleLeft = Math.Max(0, -MapTranslateTransform.X / scale);
        var visibleTop = Math.Max(0, -MapTranslateTransform.Y / scale);
        var visibleRight = Math.Min(SatisfactoryMapCoordinateConverter.MapWidth, (ActualWidth - MapTranslateTransform.X) / scale);
        var visibleBottom = Math.Min(SatisfactoryMapCoordinateConverter.MapHeight, (ActualHeight - MapTranslateTransform.Y) / scale);
        var left = node.MapX + 18 / scale;
        var top = node.MapY - 18 / scale;

        if (left + cardWidthInMap > visibleRight - margin)
        {
            left = node.MapX - cardWidthInMap - 18 / scale;
        }

        if (top + cardHeightInMap > visibleBottom - margin)
        {
            top = visibleBottom - cardHeightInMap - margin;
        }

        if (top < visibleTop + margin)
        {
            top = visibleTop + margin;
        }

        if (left < visibleLeft + margin)
        {
            left = visibleLeft + margin;
        }

        Canvas.SetLeft(card, left);
        Canvas.SetTop(card, top);
    }

    private FrameworkElement CreateResourceEditorLine(ResourceNodeViewModel node)
    {
        var comboBox = new ComboBox
        {
            ItemsSource = _styleProvider.GetEditableResourceTypes(),
            SelectedItem = NormalizeEditableResource(node.ResourceType),
            MinWidth = 148,
            MinHeight = 26
        };

        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is not string resourceType)
            {
                return;
            }

            if (node.ResourceType.Equals(resourceType, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            node.ResourceType = resourceType;
            if (IsEmptyResource(resourceType))
            {
                node.Purity = "Not applicable";
            }
            else if (IsEmptyResource(node.Purity) || node.Purity.Equals("Not applicable", StringComparison.OrdinalIgnoreCase))
            {
                node.Purity = "Normal";
            }

            CommitNodeEdit(node);
        };

        return CreateEditorLine("Resource", comboBox);
    }

    private FrameworkElement CreateWellResourceEditorLine(ResourceNodeViewModel node)
    {
        var comboBox = new ComboBox
        {
            ItemsSource = _styleProvider.GetWellResourceTypes(),
            SelectedItem = NormalizeEditableResource(node.ResourceType),
            MinWidth = 148,
            MinHeight = 26
        };

        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is not string resourceType)
            {
                return;
            }

            if (node.ResourceType.Equals(resourceType, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            node.ResourceType = resourceType;
            CommitNodeEdit(node);
        };

        return CreateEditorLine("Resource", comboBox);
    }

    private FrameworkElement CreatePurityEditorLine(ResourceNodeViewModel node)
    {
        if (IsEmptyResource(node.ResourceType))
        {
            return CreateInfoLine("Purity", "Not applicable");
        }

        var comboBox = new ComboBox
        {
            ItemsSource = new[] { "Impure", "Normal", "Pure" },
            SelectedItem = NormalizeEditablePurity(node.Purity),
            MinWidth = 148,
            MinHeight = 26
        };

        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is not string purity)
            {
                return;
            }

            if (node.Purity.Equals(purity, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            node.Purity = purity;
            CommitNodeEdit(node);
        };

        return CreateEditorLine("Purity", comboBox);
    }

    private FrameworkElement CreateSatellitePurityEditor(ResourceNodeViewModel node)
    {
        if (node.Satellites.Count == 0)
        {
            return CreateInfoLine("Satellites", "Not available");
        }

        var root = new StackPanel { Margin = new Thickness(0, 4, 0, 7) };
        var title = new TextBlock
        {
            Text = "Satellites",
            Foreground = GetBrushResource("AppMutedTextBrush", SystemColors.GrayTextBrush),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        };
        root.Children.Add(title);

        foreach (var satellite in node.Satellites)
        {
            var comboBox = new ComboBox
            {
                ItemsSource = new[] { "Impure", "Normal", "Pure" },
                SelectedItem = NormalizeEditablePurity(satellite.Purity),
                MinWidth = 132,
                MinHeight = 26
            };

            comboBox.SelectionChanged += (_, _) =>
            {
                if (comboBox.SelectedItem is not string purity)
                {
                    return;
                }

                if (satellite.Purity.Equals(purity, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                satellite.Purity = purity;
                CommitNodeEdit(node);
            };

            root.Children.Add(CreateEditorLine(satellite.DisplayName, comboBox));
        }

        return root;
    }

    private FrameworkElement CreateEditorLine(string label, FrameworkElement editor)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = GetBrushResource("AppMutedTextBrush", SystemColors.GrayTextBrush),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(editor);
        return grid;
    }

    private void CommitNodeEdit(ResourceNodeViewModel node)
    {
        if (NodeEditedCommand?.CanExecute(node) == true)
        {
            NodeEditedCommand.Execute(node);
        }

        RenderMap();
        ShowNodeCard(node);
    }

    private bool TryStartBrushPaint(MouseButtonEventArgs args)
    {
        if (args.ChangedButton is not (MouseButton.Left or MouseButton.Right) || !IsBrushModifierActive())
        {
            return false;
        }

        CloseNodeCard();
        _isBrushPainting = true;
        _isBrushErasing = args.ChangedButton == MouseButton.Right;
        _brushEditedNodes.Clear();
        MapCanvas.CaptureMouse();
        Cursor = _isBrushErasing ? Cursors.No : Cursors.Cross;
        _lastMousePoint = args.GetPosition(this);
        UpdateBrushVisual(_lastMousePoint);
        PaintBrushAt(_lastMousePoint);
        args.Handled = true;
        return true;
    }

    private void PickBrushFromNode(ResourceNodeViewModel node, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Middle || !CanEditNode(node))
        {
            return;
        }

        BrushResourceType = NormalizeEditableResource(node.ResourceType);
        if (!IsEmptyResource(node.ResourceType))
        {
            BrushPurity = NormalizeEditablePurity(node.Purity);
        }

        args.Handled = true;
    }

    private void PaintBrushAt(Point screenPoint)
    {
        if (!_isBrushErasing && !IsBrushResourceChangeEnabled && !IsBrushPurityChangeEnabled)
        {
            return;
        }

        var radius = Math.Max(1, BrushSizePixels / 2);
        var radiusSquared = radius * radius;
        var changedNodes = new List<ResourceNodeViewModel>();
        foreach (var node in Nodes?.Cast<ResourceNodeViewModel>() ?? [])
        {
            if (!CanEditNode(node))
            {
                continue;
            }

            var nodeScreen = MapPointToScreen(node.MapX, node.MapY);
            var dx = nodeScreen.X - screenPoint.X;
            var dy = nodeScreen.Y - screenPoint.Y;
            if ((dx * dx) + (dy * dy) > radiusSquared)
            {
                continue;
            }

            if (ApplyBrushToNode(node, _isBrushErasing))
            {
                changedNodes.Add(node);
                if (_resourceMarkerVisuals.TryGetValue(node.Id, out var marker))
                {
                    UpdateResourceNodeVisual(marker, node);
                }
            }
        }

        if (changedNodes.Count == 0)
        {
            return;
        }

        foreach (var node in changedNodes)
        {
            _brushEditedNodes.Add(node);
        }

        UpdateBrushVisual(screenPoint);
    }

    private void FinishBrushStroke(Point screenPoint)
    {
        if (!_isBrushPainting)
        {
            return;
        }

        _isBrushPainting = false;
        _isBrushErasing = false;
        if (_brushEditedNodes.Count > 0)
        {
            var editedNode = _brushEditedNodes.First();
            if (NodeEditedCommand?.CanExecute(editedNode) == true)
            {
                NodeEditedCommand.Execute(editedNode);
            }

            _brushEditedNodes.Clear();
            RenderMap();
        }

        UpdateBrushVisual(screenPoint);
    }

    private bool ApplyBrushToNode(ResourceNodeViewModel node, bool erase)
    {
        if (erase)
        {
            var erased = false;
            if (!node.ResourceType.Equals("Empty", StringComparison.OrdinalIgnoreCase))
            {
                node.ResourceType = "Empty";
                erased = true;
            }

            if (!node.Purity.Equals("Not applicable", StringComparison.OrdinalIgnoreCase))
            {
                node.Purity = "Not applicable";
                erased = true;
            }

            return erased;
        }

        var changed = false;
        if (IsBrushResourceChangeEnabled)
        {
            var resourceType = NormalizeEditableResource(BrushResourceType);
            if (!node.ResourceType.Equals(resourceType, StringComparison.OrdinalIgnoreCase))
            {
                node.ResourceType = resourceType;
                changed = true;
            }

            if (IsEmptyResource(resourceType) && !node.Purity.Equals("Not applicable", StringComparison.OrdinalIgnoreCase))
            {
                node.Purity = "Not applicable";
                changed = true;
            }
            else if (!IsEmptyResource(resourceType) && node.Purity.Equals("Not applicable", StringComparison.OrdinalIgnoreCase))
            {
                node.Purity = "Normal";
                changed = true;
            }
        }

        if (IsBrushPurityChangeEnabled && !IsEmptyResource(node.ResourceType))
        {
            var purity = NormalizeEditablePurity(BrushPurity);
            if (!node.Purity.Equals(purity, StringComparison.OrdinalIgnoreCase))
            {
                node.Purity = purity;
                changed = true;
            }
        }

        return changed;
    }

    private Point MapPointToScreen(double mapX, double mapY) => new(
        mapX * MapScaleTransform.ScaleX + MapTranslateTransform.X,
        mapY * MapScaleTransform.ScaleY + MapTranslateTransform.Y);

    private void UpdateBrushVisual(Point screenPoint)
    {
        if (!IsBrushModifierActive() || !IsMouseOver)
        {
            BrushCircle.Visibility = Visibility.Collapsed;
            return;
        }

        var size = Math.Clamp(BrushSizePixels, 8, 128);
        BrushCircle.Width = size;
        BrushCircle.Height = size;
        BrushCircle.Visibility = Visibility.Visible;
        BrushCircle.RenderTransform = new TranslateTransform(screenPoint.X - size / 2, screenPoint.Y - size / 2);
    }

    private static bool IsBrushModifierActive() =>
        (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

    private void FitToMapButton_Click(object sender, RoutedEventArgs e)
    {
        CloseNodeCard();
        FitMapToViewport();
        e.Handled = true;
    }

    private void FitMapToViewport()
    {
        var availableWidth = Math.Max(1, ActualWidth - 24);
        var availableHeight = Math.Max(1, ActualHeight - 24);
        var mapWidth = SatisfactoryMapCoordinateConverter.MapWidth;
        var mapHeight = SatisfactoryMapCoordinateConverter.MapHeight;
        var scale = Math.Clamp(Math.Min(availableWidth / mapWidth, availableHeight / mapHeight), MinZoom, MaxZoom);

        MapScaleTransform.ScaleX = scale;
        MapScaleTransform.ScaleY = scale;
        MapTranslateTransform.X = (ActualWidth - mapWidth * scale) / 2;
        MapTranslateTransform.Y = (ActualHeight - mapHeight * scale) / 2;
        ApplyMarkerScreenScale();
        UpdateBrushVisual(_lastMousePoint);
    }

    private void InputManager_PreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (!IsMouseOver)
        {
            return;
        }

        if (e.StagingItem.Input is KeyEventArgs keyArgs &&
            (keyArgs.Key is Key.LeftCtrl or Key.RightCtrl ||
             keyArgs.SystemKey is Key.LeftCtrl or Key.RightCtrl))
        {
            Dispatcher.BeginInvoke(() => UpdateBrushVisual(Mouse.GetPosition(this)));
        }
    }

    private static string GetCardTitle(ResourceNodeViewModel node)
    {
        if (IsGeyser(node))
        {
            return "Geyser";
        }

        if (IsWell(node))
        {
            return "Fracking Well";
        }

        return DisplayValue(node.ResourceType);
    }

    private static string DisplayValue(string value) => string.IsNullOrWhiteSpace(value) || value.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
        ? "Not available"
        : value;

    private static FrameworkElement CreateInfoLine(string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = GetBrushResource("AppMutedTextBrush", SystemColors.GrayTextBrush),
            FontWeight = FontWeights.SemiBold
        };
        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = GetBrushResource("AppTextBrush", SystemColors.WindowTextBrush),
            TextWrapping = TextWrapping.Wrap
        };

        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
        return grid;
    }

    private void MapCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var zoomFactor = e.Delta > 0 ? 1.12 : 1 / 1.12;
        var oldScale = MapScaleTransform.ScaleX;
        var newScale = Math.Clamp(oldScale * zoomFactor, MinZoom, MaxZoom);
        if (Math.Abs(newScale - oldScale) < 0.0001)
        {
            return;
        }

        var mouse = e.GetPosition(this);
        var contentX = (mouse.X - MapTranslateTransform.X) / oldScale;
        var contentY = (mouse.Y - MapTranslateTransform.Y) / oldScale;

        MapScaleTransform.ScaleX = newScale;
        MapScaleTransform.ScaleY = newScale;
        MapTranslateTransform.X = mouse.X - contentX * newScale;
        MapTranslateTransform.Y = mouse.Y - contentY * newScale;
        ApplyMarkerScreenScale();
        e.Handled = true;
    }

    private void ApplyMarkerScreenScale()
    {
        var inverseScale = 1 / MapScaleTransform.ScaleX;
        foreach (var marker in _markerVisuals)
        {
            marker.RenderTransform = new ScaleTransform(inverseScale, inverseScale);
        }
    }

    private void MapCanvas_MouseButtonDown(object sender, MouseButtonEventArgs e)
    {
        ClearPendingNodeClick();

        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Middle or MouseButton.Right))
        {
            return;
        }

        if (TryStartBrushPaint(e))
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            CloseNodeCard();
        }

        _isDragging = true;
        _lastDragPoint = e.GetPosition(this);
        MapCanvas.CaptureMouse();
        Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void MapCanvas_MouseButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Middle or MouseButton.Right))
        {
            return;
        }

        if (e.ChangedButton is MouseButton.Left or MouseButton.Right && _isBrushPainting)
        {
            FinishBrushStroke(e.GetPosition(this));
            MapCanvas.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && CompletePendingNodeClick(e.GetPosition(this)))
        {
            e.Handled = true;
            return;
        }

        ClearPendingNodeClick();
        _isDragging = false;
        MapCanvas.ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
        UpdateBrushVisual(e.GetPosition(this));
        e.Handled = true;
    }

    private void MapCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        _lastMousePoint = point;
        UpdateBrushVisual(point);
        if (_pendingNodeClick is not null)
        {
            var movement = point - _pendingNodeClickPoint;
            if (Math.Abs(movement.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(movement.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (!_pendingNodeClickMoved)
                {
                    CloseNodeCard();
                }

                _pendingNodeClickMoved = true;
            }
        }

        if (_isBrushPainting)
        {
            var expectedButtonPressed = _isBrushErasing
                ? e.RightButton == MouseButtonState.Pressed
                : e.LeftButton == MouseButtonState.Pressed;
            if (IsBrushModifierActive() && expectedButtonPressed)
            {
                PaintBrushAt(point);
            }
            else
            {
                FinishBrushStroke(point);
                MapCanvas.ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
            }

            return;
        }

        if (!_isDragging)
        {
            return;
        }

        var delta = point - _lastDragPoint;
        MapTranslateTransform.X += delta.X;
        MapTranslateTransform.Y += delta.Y;
        _lastDragPoint = point;
        UpdateBrushVisual(point);
    }

    private void MapViewport_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isBrushPainting)
        {
            BrushCircle.Visibility = Visibility.Collapsed;
        }
    }

    private static Brush GetBrushResource(string key, Brush fallback) =>
        Application.Current.TryFindResource(key) as Brush ?? fallback;
}
