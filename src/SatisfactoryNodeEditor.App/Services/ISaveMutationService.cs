using SatisfactoryNodeEditor.App.Models;
using SatisfactoryNodeEditor.App.ViewModels;

namespace SatisfactoryNodeEditor.App.Services;

public interface ISaveMutationService
{
    Task<SaveMutationResult> SaveResourceNodeAssignmentsAsync(
        string inputSavePath,
        string outputSavePath,
        IReadOnlyCollection<ResourceNodeViewModel> nodes,
        CancellationToken cancellationToken = default);
}
