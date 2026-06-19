using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SatisfactoryNodeEditor.App.ViewModels;

public class PurityDistributionViewModel : INotifyPropertyChanged
{
    private double _impurePercent = 25;
    private double _normalPercent = 50;
    private double _purePercent = 25;
    private int _totalNodeCount;

    public event PropertyChangedEventHandler? PropertyChanged;

    public double ImpurePercent
    {
        get => _impurePercent;
        set
        {
            if (SetField(ref _impurePercent, ClampPercent(value)))
            {
                NotifyCountsChanged();
            }
        }
    }

    public double NormalPercent
    {
        get => _normalPercent;
        set
        {
            if (SetField(ref _normalPercent, ClampPercent(value)))
            {
                NotifyCountsChanged();
            }
        }
    }

    public double PurePercent
    {
        get => _purePercent;
        set
        {
            if (SetField(ref _purePercent, ClampPercent(value)))
            {
                NotifyCountsChanged();
            }
        }
    }

    public int TotalNodeCount
    {
        get => _totalNodeCount;
        set
        {
            if (SetField(ref _totalNodeCount, Math.Max(0, value)))
            {
                NotifyCountsChanged();
            }
        }
    }

    public int ImpureNodeCount => CalculateImpureNodeCount();

    public int NormalNodeCount => CalculateNormalNodeCount();

    public int PureNodeCount => Math.Max(0, TotalNodeCount - ImpureNodeCount - NormalNodeCount);

    public void SetPercentages(double impurePercent, double normalPercent, double purePercent)
    {
        var total = Math.Max(0, impurePercent + normalPercent + purePercent);
        if (total <= 0)
        {
            impurePercent = 25;
            normalPercent = 50;
            purePercent = 25;
            total = 100;
        }

        _impurePercent = ClampPercent(impurePercent / total * 100);
        _normalPercent = ClampPercent(normalPercent / total * 100);
        _purePercent = Math.Max(0, 100 - _impurePercent - _normalPercent);
        OnPropertyChanged(nameof(ImpurePercent));
        OnPropertyChanged(nameof(NormalPercent));
        OnPropertyChanged(nameof(PurePercent));
        NotifyCountsChanged();
    }

    private int CalculateImpureNodeCount() => (int)Math.Round(TotalNodeCount * ImpurePercent / 100.0, MidpointRounding.AwayFromZero);

    private int CalculateNormalNodeCount()
    {
        var remaining = Math.Max(0, TotalNodeCount - ImpureNodeCount);
        return Math.Min(remaining, (int)Math.Round(TotalNodeCount * NormalPercent / 100.0, MidpointRounding.AwayFromZero));
    }

    private void NotifyCountsChanged()
    {
        OnPropertyChanged(nameof(ImpureNodeCount));
        OnPropertyChanged(nameof(NormalNodeCount));
        OnPropertyChanged(nameof(PureNodeCount));
    }

    private static double ClampPercent(double value) => Math.Clamp(value, 0, 100);

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
