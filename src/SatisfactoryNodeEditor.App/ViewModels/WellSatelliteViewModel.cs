using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SatisfactoryNodeEditor.App.ViewModels;

public sealed class WellSatelliteViewModel : INotifyPropertyChanged
{
    private string _purity = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Purity
    {
        get => _purity;
        set => SetField(ref _purity, value);
    }

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
