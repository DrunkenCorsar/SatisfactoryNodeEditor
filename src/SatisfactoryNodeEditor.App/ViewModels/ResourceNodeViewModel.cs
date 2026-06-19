using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace SatisfactoryNodeEditor.App.ViewModels;

public sealed class ResourceNodeViewModel : INotifyPropertyChanged
{
    private string _resourceType = string.Empty;
    private string _purity = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; init; } = string.Empty;
    public string NodeKind { get; init; } = "ResourceNode";
    public string ResourceType
    {
        get => _resourceType;
        set => SetField(ref _resourceType, value);
    }

    public string Purity
    {
        get => _purity;
        set => SetField(ref _purity, value);
    }

    public double WorldX { get; init; }
    public double WorldY { get; init; }
    public double WorldZ { get; init; }
    public double MapX { get; set; }
    public double MapY { get; set; }
    public ObservableCollection<WellSatelliteViewModel> Satellites { get; init; } = [];

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
