using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using SatisfactoryNodeEditor.App.Models;
using SatisfactoryNodeEditor.App.ViewModels;

namespace SatisfactoryNodeEditor.App.Services;

public sealed class ExternalSaveMutationService : ISaveMutationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<SaveMutationResult> SaveResourceNodeAssignmentsAsync(
        string inputSavePath,
        string outputSavePath,
        IReadOnlyCollection<ResourceNodeViewModel> nodes,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputSavePath))
        {
            return Failure(outputSavePath, $"Input save does not exist: {inputSavePath}");
        }

        if (Path.GetFullPath(inputSavePath).Equals(Path.GetFullPath(outputSavePath), StringComparison.OrdinalIgnoreCase))
        {
            return Failure(outputSavePath, "Output path must not overwrite the original save.");
        }

        var workerPath = ResolveWorkerPath();
        if (workerPath is null)
        {
            return Failure(outputSavePath, "Could not find SaveWorker/mutate-save.js beside the app or in the source tree.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputSavePath)!);
        var assignmentPath = Path.Combine(Path.GetTempPath(), $"satisfactory-node-assignments-{Guid.NewGuid():N}.json");
        var assignments = nodes
            .Where(node =>
                node.NodeKind.Equals("ResourceNode", StringComparison.OrdinalIgnoreCase) ||
                node.NodeKind.Equals("Well", StringComparison.OrdinalIgnoreCase))
            .Select(node => new ResourceNodeAssignment
            {
                Id = node.Id,
                ResourceType = node.ResourceType,
                Purity = node.Purity,
                Satellites = node.Satellites
                    .Select(satellite => new WellSatelliteAssignment
                    {
                        Id = satellite.Id,
                        Purity = satellite.Purity
                    })
                    .ToArray()
            })
            .ToArray();
        await File.WriteAllTextAsync(assignmentPath, JsonSerializer.Serialize(assignments), cancellationToken);

        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            WorkingDirectory = Path.GetDirectoryName(workerPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(workerPath);
        startInfo.ArgumentList.Add("apply-assignments");
        startInfo.ArgumentList.Add(inputSavePath);
        startInfo.ArgumentList.Add(outputSavePath);
        startInfo.ArgumentList.Add(assignmentPath);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdout.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stderr.AppendLine(args.Data);
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return Failure(outputSavePath, $"Failed to start Node worker. Is Node.js installed? {ex.Message}");
        }

        try
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);

            var stdoutText = stdout.ToString();
            var stderrText = stderr.ToString();
            var result = TryReadResult(stdoutText);
            if (result is not null)
            {
                return new SaveMutationResult
                {
                    Success = process.ExitCode == 0 && result.Success,
                    OutputSavePath = string.IsNullOrWhiteSpace(result.OutputSavePath) ? outputSavePath : result.OutputSavePath,
                    CandidateNodesFound = result.CandidateNodesFound,
                    NodesChanged = result.NodesChanged,
                    Log = CombineLogs(result.Log, stderrText),
                    ErrorMessage = process.ExitCode == 0 ? result.ErrorMessage : result.ErrorMessage ?? $"Worker exited with code {process.ExitCode}."
                };
            }

            return new SaveMutationResult
            {
                Success = false,
                OutputSavePath = outputSavePath,
                Log = CombineLogs(stdoutText, stderrText),
                ErrorMessage = $"Worker exited with code {process.ExitCode} and did not return parseable JSON."
            };
        }
        finally
        {
            TryDelete(assignmentPath);
        }
    }

    private static SaveMutationResult? TryReadResult(string stdout)
    {
        var trimmed = stdout.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SaveMutationResult>(trimmed, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SaveMutationResult Failure(string outputSavePath, string message) => new()
    {
        Success = false,
        OutputSavePath = outputSavePath,
        ErrorMessage = message,
        Log = message
    };

    private static string CombineLogs(string first, string second)
    {
        var parts = new[] { first.Trim(), second.Trim() }.Where(part => !string.IsNullOrWhiteSpace(part));
        return string.Join(Environment.NewLine, parts);
    }

    private static string? ResolveWorkerPath()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "SaveWorker", "mutate-save.js")),
            Path.Combine(baseDirectory, "SaveWorker", "mutate-save.js")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Temp assignment files can be cleaned up later if still locked.
        }
    }
}
