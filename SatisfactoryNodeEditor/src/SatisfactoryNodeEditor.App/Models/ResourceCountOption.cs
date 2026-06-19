using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SatisfactoryNodeEditor.App.Models;

public sealed class ResourceCountOption : INotifyPropertyChanged
{
    private int _count;
    private double _percentage;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ResourceKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int DefaultCount { get; set; }
    public int Count
    {
        get => _count;
        set
        {
            var normalized = Math.Max(0, value);
            if (_count == normalized)
            {
                return;
            }

            _count = normalized;
            OnPropertyChanged();
        }
    }

    public double Percentage
    {
        get => _percentage;
        set
        {
            if (Math.Abs(_percentage - value) < 0.0001)
            {
                return;
            }

            _percentage = value;
            OnPropertyChanged();
        }
    }

    public string IconPath { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
