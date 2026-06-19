using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SatisfactoryNodeEditor.App.Controls;

public partial class ThreeWayPuritySlider : UserControl
{
    public static readonly DependencyProperty ImpurePercentProperty = DependencyProperty.Register(
        nameof(ImpurePercent),
        typeof(double),
        typeof(ThreeWayPuritySlider),
        new FrameworkPropertyMetadata(25.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnDistributionChanged));

    public static readonly DependencyProperty NormalPercentProperty = DependencyProperty.Register(
        nameof(NormalPercent),
        typeof(double),
        typeof(ThreeWayPuritySlider),
        new FrameworkPropertyMetadata(50.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnDistributionChanged));

    public static readonly DependencyProperty PurePercentProperty = DependencyProperty.Register(
        nameof(PurePercent),
        typeof(double),
        typeof(ThreeWayPuritySlider),
        new FrameworkPropertyMetadata(25.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnDistributionChanged));

    private const double HandleWidth = 14;
    private const double TrackTop = 9;
    private const double HandleTop = 5;
    private bool _draggingFirstHandle;
    private bool _draggingSecondHandle;
    private bool _isNormalizing;

    public ThreeWayPuritySlider()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateVisuals();
    }

    public double ImpurePercent
    {
        get => (double)GetValue(ImpurePercentProperty);
        set => SetValue(ImpurePercentProperty, value);
    }

    public double NormalPercent
    {
        get => (double)GetValue(NormalPercentProperty);
        set => SetValue(NormalPercentProperty, value);
    }

    public double PurePercent
    {
        get => (double)GetValue(PurePercentProperty);
        set => SetValue(PurePercentProperty, value);
    }

    private static void OnDistributionChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is ThreeWayPuritySlider slider)
        {
            slider.NormalizeDistribution();
            slider.UpdateVisuals();
        }
    }

    private void NormalizeDistribution()
    {
        if (_isNormalizing)
        {
            return;
        }

        _isNormalizing = true;
        var impure = Math.Clamp(ImpurePercent, 0, 100);
        var normal = Math.Clamp(NormalPercent, 0, 100 - impure);
        var pure = Math.Max(0, 100 - impure - normal);

        SetCurrentValue(ImpurePercentProperty, impure);
        SetCurrentValue(NormalPercentProperty, normal);
        SetCurrentValue(PurePercentProperty, pure);
        _isNormalizing = false;
    }

    private void UpdateVisuals()
    {
        if (SliderCanvas is null)
        {
            return;
        }

        var width = Math.Max(0, SliderCanvas.ActualWidth);
        TrackBorder.Width = width;
        Canvas.SetTop(TrackBorder, TrackTop);
        Canvas.SetLeft(TrackBorder, 0);

        ImpureColumn.Width = new GridLength(Math.Max(0.001, ImpurePercent), GridUnitType.Star);
        NormalColumn.Width = new GridLength(Math.Max(0.001, NormalPercent), GridUnitType.Star);
        PureColumn.Width = new GridLength(Math.Max(0.001, PurePercent), GridUnitType.Star);

        var firstX = width * ImpurePercent / 100.0;
        var secondX = width * (ImpurePercent + NormalPercent) / 100.0;
        var firstLeft = Math.Clamp(firstX - HandleWidth / 2, 0, Math.Max(0, width - HandleWidth));
        var secondLeft = Math.Clamp(secondX - HandleWidth / 2, 0, Math.Max(0, width - HandleWidth));
        if (Math.Abs(firstX - secondX) <= 0.001 && width >= HandleWidth * 2)
        {
            firstLeft = Math.Clamp(firstX - HandleWidth, 0, width - HandleWidth * 2);
            secondLeft = firstLeft + HandleWidth;
        }

        Canvas.SetLeft(FirstHandle, firstLeft);
        Canvas.SetLeft(SecondHandle, secondLeft);
        Canvas.SetTop(FirstHandle, HandleTop);
        Canvas.SetTop(SecondHandle, HandleTop);
    }

    private void SliderCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateVisuals();

    private void SliderCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(SliderCanvas);
        if (!TryBeginDragFromPoint(point.X))
        {
            return;
        }

        e.Handled = true;
    }

    private void FirstHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        TryBeginDragFromPoint(e.GetPosition(SliderCanvas).X, preferFirstWhenOverlapping: true);
        e.Handled = true;
    }

    private void SecondHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        TryBeginDragFromPoint(e.GetPosition(SliderCanvas).X, preferFirstWhenOverlapping: false);
        e.Handled = true;
    }

    private bool TryBeginDragFromPoint(double x, bool? preferFirstWhenOverlapping = null)
    {
        var width = Math.Max(1, SliderCanvas.ActualWidth);
        var firstX = width * ImpurePercent / 100.0;
        var secondX = width * (ImpurePercent + NormalPercent) / 100.0;
        if (Math.Abs(firstX - secondX) <= 0.001 && width >= HandleWidth * 2)
        {
            var firstLeft = Math.Clamp(firstX - HandleWidth, 0, width - HandleWidth * 2);
            firstX = firstLeft + HandleWidth / 2;
            secondX = firstLeft + HandleWidth + HandleWidth / 2;
        }

        const double hitPadding = 10;
        var firstDistance = Math.Abs(x - firstX);
        var secondDistance = Math.Abs(x - secondX);
        var firstHit = firstDistance <= HandleWidth / 2 + hitPadding;
        var secondHit = secondDistance <= HandleWidth / 2 + hitPadding;

        if (!firstHit && !secondHit)
        {
            return false;
        }

        _draggingFirstHandle = false;
        _draggingSecondHandle = false;

        if (firstHit && secondHit)
        {
            var handlesOverlap = Math.Abs(firstX - secondX) <= HandleWidth;
            var chooseFirst = handlesOverlap
                ? preferFirstWhenOverlapping ?? x <= firstX
                : firstDistance <= secondDistance;
            _draggingFirstHandle = chooseFirst;
            _draggingSecondHandle = !chooseFirst;
        }
        else
        {
            _draggingFirstHandle = firstHit;
            _draggingSecondHandle = secondHit;
        }

        Panel.SetZIndex(FirstHandle, _draggingFirstHandle ? 3 : 2);
        Panel.SetZIndex(SecondHandle, _draggingSecondHandle ? 3 : 2);
        SliderCanvas.CaptureMouse();
        return true;
    }

    private void SliderCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_draggingFirstHandle && !_draggingSecondHandle)
        {
            return;
        }

        var width = Math.Max(1, SliderCanvas.ActualWidth);
        var percent = Math.Clamp(e.GetPosition(SliderCanvas).X / width * 100.0, 0, 100);
        var first = ImpurePercent;
        var second = ImpurePercent + NormalPercent;

        if (_draggingFirstHandle)
        {
            first = Math.Min(percent, second);
        }
        else
        {
            second = Math.Max(percent, first);
        }

        SetDistribution(first, second - first, 100 - second);
        e.Handled = true;
    }

    private void SliderCanvas_MouseLeftButtonUp(object sender, MouseEventArgs e)
    {
        _draggingFirstHandle = false;
        _draggingSecondHandle = false;
        Panel.SetZIndex(FirstHandle, 2);
        Panel.SetZIndex(SecondHandle, 2);
        SliderCanvas.ReleaseMouseCapture();
    }

    private void SetDistribution(double impure, double normal, double pure)
    {
        _isNormalizing = true;
        SetCurrentValue(ImpurePercentProperty, Math.Clamp(impure, 0, 100));
        SetCurrentValue(NormalPercentProperty, Math.Clamp(normal, 0, 100));
        SetCurrentValue(PurePercentProperty, Math.Clamp(pure, 0, 100));
        _isNormalizing = false;
        UpdateVisuals();
    }
}
