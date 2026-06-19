using System.Windows;
using SatisfactoryNodeEditor.App.ViewModels;

namespace SatisfactoryNodeEditor.App.Services;

public sealed class SatisfactoryMapCoordinateConverter : IMapCoordinateConverter
{
    public const double MapWidth = 5000;
    public const double MapHeight = 5000;
    private const double WorldMinX = -324600;
    private const double WorldMaxX = 425300;
    private const double WorldMinY = -375000;
    private const double WorldMaxY = 375000;

    public string LastCalibrationLog { get; private set; } = "Map coordinate converter has not been calibrated yet.";

    public void Calibrate(IEnumerable<ResourceNodeViewModel> nodes)
    {
        var materialized = nodes.ToArray();
        if (materialized.Length == 0)
        {
            LastCalibrationLog = "No resource nodes available for coordinate calibration.";
            return;
        }

        var minX = materialized.Min(node => node.WorldX);
        var maxX = materialized.Max(node => node.WorldX);
        var minY = materialized.Min(node => node.WorldY);
        var maxY = materialized.Max(node => node.WorldY);
        LastCalibrationLog = $"Node coordinate range: X {minX:0.##}..{maxX:0.##}, Y {minY:0.##}..{maxY:0.##}. Projected using fixed world bounds X {WorldMinX:0}..{WorldMaxX:0}, Y {WorldMinY:0}..{WorldMaxY:0}.";
    }

    public Point WorldToMap(double worldX, double worldY)
    {
        var x = ((worldX - WorldMinX) / (WorldMaxX - WorldMinX)) * MapWidth;
        var y = ((worldY - WorldMinY) / (WorldMaxY - WorldMinY)) * MapHeight;
        return new Point(x, y);
    }
}
