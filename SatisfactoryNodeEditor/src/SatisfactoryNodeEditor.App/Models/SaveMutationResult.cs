namespace SatisfactoryNodeEditor.App.Models;

public sealed class SaveMutationResult
{
    public bool Success { get; init; }
    public string OutputSavePath { get; init; } = string.Empty;
    public int CandidateNodesFound { get; init; }
    public int NodesChanged { get; init; }
    public string Log { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
}
