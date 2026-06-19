namespace SatisfactoryNodeEditor.App.Models;

public sealed class ResourceNodeAssignment
{
    public string Id { get; init; } = string.Empty;
    public string ResourceType { get; init; } = string.Empty;
    public string Purity { get; init; } = string.Empty;
    public IReadOnlyList<WellSatelliteAssignment> Satellites { get; init; } = [];
}
