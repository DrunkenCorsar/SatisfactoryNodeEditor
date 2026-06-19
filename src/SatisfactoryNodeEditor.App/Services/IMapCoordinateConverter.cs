using System.Windows;

namespace SatisfactoryNodeEditor.App.Services;

public interface IMapCoordinateConverter
{
    Point WorldToMap(double worldX, double worldY);
}
