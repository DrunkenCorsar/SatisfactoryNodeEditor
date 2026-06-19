using System.Windows.Media;

namespace SatisfactoryNodeEditor.App.ViewModels;

public sealed class ResourcePurityDistributionViewModel : PurityDistributionViewModel
{
    public string ResourceName { get; init; } = string.Empty;

    public Brush ResourceBrush { get; init; } = Brushes.Transparent;
}
