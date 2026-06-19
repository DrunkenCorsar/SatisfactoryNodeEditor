using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SatisfactoryNodeEditor.App.Models;

namespace SatisfactoryNodeEditor.App.Services;

public sealed class ResourceNodeStyleProvider
{
    private static readonly IReadOnlyDictionary<string, string> ResourceColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Iron"] = "#9aa0a6",
        ["Copper"] = "#c87533",
        ["Limestone"] = "#d9d1b3",
        ["Coal"] = "#343a40",
        ["Caterium"] = "#d5a72d",
        ["Sulfur"] = "#f3db3f",
        ["Quartz"] = "#d8fbff",
        ["Bauxite"] = "#dc5b2e",
        ["Uranium"] = "#49b75c",
        ["SAM"] = "#9b63d8",
        ["Crude Oil"] = "#064b76",
        ["Water"] = "#2776d8",
        ["Nitrogen"] = "#8dd9ff",
        ["Geothermal"] = "#56e0c6",
        ["Raw Quartz"] = "#d8fbff",
        ["Empty"] = "#ffffff",
        ["Unknown"] = "#7b8494"
    };

    private static readonly IReadOnlyDictionary<string, string> ResourceLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Iron"] = "Fe",
        ["Copper"] = "Cu",
        ["Limestone"] = "Lm",
        ["Coal"] = "Coal",
        ["Caterium"] = "Cat",
        ["Sulfur"] = "S",
        ["Quartz"] = "Qz",
        ["Bauxite"] = "Baux",
        ["Uranium"] = "U",
        ["SAM"] = "SAM",
        ["Crude Oil"] = "Oil",
        ["Water"] = "H2O",
        ["Nitrogen"] = "N2",
        ["Geothermal"] = "Geo",
        ["Raw Quartz"] = "Qz",
        ["Empty"] = "X",
        ["Unknown"] = "?"
    };

    public Brush GetResourceBrush(string resourceType) => ToBrush(ResourceColors.TryGetValue(resourceType, out var color) ? color : ResourceColors["Unknown"]);

    public IEnumerable<string> GetLegendResourceTypes() =>
    [
        "Iron",
        "Copper",
        "Limestone",
        "Coal",
        "Caterium",
        "Sulfur",
        "Bauxite",
        "Raw Quartz",
        "Uranium",
        "SAM",
        "Crude Oil",
        "Water",
        "Nitrogen"
    ];

    public IEnumerable<string> GetEditableResourceTypes() =>
    [
        "Iron",
        "Copper",
        "Limestone",
        "Coal",
        "Caterium",
        "Sulfur",
        "Bauxite",
        "Raw Quartz",
        "Uranium",
        "SAM",
        "Crude Oil",
        "Empty"
    ];

    public IEnumerable<string> GetWellResourceTypes() =>
    [
        "Water",
        "Nitrogen",
        "Crude Oil"
    ];

    public Brush GetPurityBrush(string purity) => ParsePurity(purity) switch
    {
        ResourcePurity.Impure => ToBrush("#d64545"),
        ResourcePurity.Normal => ToBrush("#e59b35"),
        ResourcePurity.Pure => ToBrush("#3bb35d"),
        _ => ToBrush("#697386")
    };

    public Geometry GetSimpleShapeGeometry(string purity, double size)
    {
        var half = size / 2;
        return ParsePurity(purity) switch
        {
            ResourcePurity.Impure => Geometry.Parse(FormattableString.Invariant($"M {half},0 L {size},{size} L 0,{size} Z")),
            ResourcePurity.Normal => new RectangleGeometry(new Rect(0, 0, size, size)),
            ResourcePurity.Pure => new EllipseGeometry(new Point(half, half), half, half),
            _ => new EllipseGeometry(new Point(half, half), half, half)
        };
    }

    public string GetResourceShortLabel(string resourceType) => ResourceLabels.TryGetValue(resourceType, out var label) ? label : "?";

    public ImageSource? GetResourceIcon(string resourceType) => null;

    private static ResourcePurity ParsePurity(string purity) => purity.Trim().ToLower(CultureInfo.InvariantCulture) switch
    {
        "impure" or "inpure" or "rp_inpure" => ResourcePurity.Impure,
        "normal" or "rp_normal" => ResourcePurity.Normal,
        "pure" or "rp_pure" => ResourcePurity.Pure,
        _ => ResourcePurity.Unknown
    };

    private static Brush ToBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
