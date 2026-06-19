namespace SatisfactoryNodeEditor.App.Models;

public sealed class SaveMutationRequest
{
    public string InputSavePath { get; init; } = string.Empty;
    public string OutputSavePath { get; init; } = string.Empty;
    public IReadOnlyList<ResourceNodeDto> Nodes { get; init; } = Array.Empty<ResourceNodeDto>();
}
