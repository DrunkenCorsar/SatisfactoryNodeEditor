using System.IO;

namespace SatisfactoryNodeEditor.App.Services;

public static class SaveWorkerRuntime
{
    public static string? ResolveWorkerScript(string scriptName)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "SaveWorker", scriptName),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "SaveWorker", scriptName))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public static string ResolveNodeExecutable()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "Tools", "node", "node.exe"),
            Path.Combine(baseDirectory, "Runtime", "node", "node.exe")
        };

        return candidates.FirstOrDefault(File.Exists) ?? "node";
    }

    public static string FormatNodeStartFailure(Exception exception)
    {
        var expectedPath = Path.Combine(AppContext.BaseDirectory, "Tools", "node", "node.exe");
        return $"Failed to start the save worker runtime. For release builds, make sure Node is bundled at {expectedPath}. Developer builds can also use Node from PATH. {exception.Message}";
    }
}
