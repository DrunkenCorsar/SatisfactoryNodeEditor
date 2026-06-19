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

        if (!Path.GetExtension(inputSavePath).Equals(".sav", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(outputSavePath, "Input file must be a Satisfactory .sav file.");
        }

        if (!Path.GetExtension(outputSavePath).Equals(".sav", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(outputSavePath, "Output file must use the .sav extension.");
        }

        if (Path.GetFullPath(inputSavePath).Equals(Path.GetFullPath(outputSavePath), StringComparison.OrdinalIgnoreCase))
        {
            return Failure(outputSavePath, "Output path must not overwrite the original save.");
        }

        var workerPath = SaveWorkerRuntime.ResolveWorkerScript("mutate-save.js");
        if (workerPath is null)
        {
            return Failure(outputSavePath, "Could not find SaveWorker/mutate-save.js beside the app or in the source tree.");
        }

        var outputDirectory = Path.GetDirectoryName(outputSavePath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return Failure(outputSavePath, "Output path must include a folder.");
        }

        try
        {
            Directory.CreateDirectory(outputDirectory);
            VerifyOutputDirectoryWritable(outputDirectory);
        }
        catch (Exception ex)
        {
            return Failure(outputSavePath, $"Output folder is not writable. No changes were written. {ex.Message}");
        }

        string backupPath;
        try
        {
            backupPath = CreateInputBackup(inputSavePath);
        }
        catch (Exception ex)
        {
            return Failure(outputSavePath, $"Could not create a backup copy of the input save. No changes were written. {ex.Message}");
        }

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
            FileName = SaveWorkerRuntime.ResolveNodeExecutable(),
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
            return Failure(outputSavePath, SaveWorkerRuntime.FormatNodeStartFailure(ex), backupPath);
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
                    BackupSavePath = backupPath,
                    CandidateNodesFound = result.CandidateNodesFound,
                    NodesChanged = result.NodesChanged,
                    Log = CombineLogs($"Backup created: {backupPath}", result.Log, stderrText),
                    ErrorMessage = process.ExitCode == 0 ? result.ErrorMessage : result.ErrorMessage ?? $"Worker exited with code {process.ExitCode}."
                };
            }

            return new SaveMutationResult
            {
                Success = false,
                OutputSavePath = outputSavePath,
                BackupSavePath = backupPath,
                Log = CombineLogs($"Backup created: {backupPath}", stdoutText, stderrText),
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

    private static SaveMutationResult Failure(string outputSavePath, string message, string backupPath = "") => new()
    {
        Success = false,
        OutputSavePath = outputSavePath,
        BackupSavePath = backupPath,
        ErrorMessage = message,
        Log = string.IsNullOrWhiteSpace(backupPath)
            ? message
            : CombineLogs($"Backup created: {backupPath}", message)
    };

    private static string CombineLogs(params string[] entries)
    {
        var parts = entries.Select(entry => entry.Trim()).Where(part => !string.IsNullOrWhiteSpace(part));
        return string.Join(Environment.NewLine, parts);
    }

    private static void VerifyOutputDirectoryWritable(string outputDirectory)
    {
        var probePath = Path.Combine(outputDirectory, $".sne-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probePath, "write-test");
        }
        finally
        {
            TryDelete(probePath);
        }
    }

    private static string CreateInputBackup(string inputSavePath)
    {
        var backupDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SatisfactoryNodeEditor",
            "backups");
        Directory.CreateDirectory(backupDirectory);

        var name = Path.GetFileNameWithoutExtension(inputSavePath);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var backupPath = Path.Combine(backupDirectory, $"{name}_original_{timestamp}.sav");
        File.Copy(inputSavePath, backupPath, overwrite: false);
        return backupPath;
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
