namespace SatisfactoryNodeEditor.App.Models;

public sealed class ResourceNodeDto
{
    public string NodeId { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public ResourceType OriginalResource { get; init; }
    public ResourceType SelectedResource { get; set; }
    public string? Purity { get; init; }
}
