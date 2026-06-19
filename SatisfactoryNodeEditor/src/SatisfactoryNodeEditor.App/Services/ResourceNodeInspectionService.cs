using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using SatisfactoryNodeEditor.App.ViewModels;

namespace SatisfactoryNodeEditor.App.Services;

public sealed class ResourceNodeInspectionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SatisfactoryMapCoordinateConverter _coordinateConverter;

    public ResourceNodeInspectionService(SatisfactoryMapCoordinateConverter coordinateConverter)
    {
        _coordinateConverter = coordinateConverter;
    }

    public async Task<ResourceNodeInspectionResult> InspectNodesAsync(string inputSavePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputSavePath))
        {
            return ResourceNodeInspectionResult.Failure($"Input save does not exist: {inputSavePath}");
        }

        var workerPath = SaveWorkerRuntime.ResolveWorkerScript("inspect-nodes.js");
        if (workerPath is null)
        {
            return ResourceNodeInspectionResult.Failure("Could not find SaveWorker/inspect-nodes.js beside the app or in the source tree.");
        }

        var outputPath = Path.Combine(Path.GetTempPath(), $"satisfactory-nodes-{Guid.NewGuid():N}.json");
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
        startInfo.ArgumentList.Add(inputSavePath);
        startInfo.ArgumentList.Add(outputPath);

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
            return ResourceNodeInspectionResult.Failure(SaveWorkerRuntime.FormatNodeStartFailure(ex));
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            return ResourceNodeInspectionResult.Failure(
                BuildNodeInspectionFailureMessage(process.ExitCode, stdout.ToString(), stderr.ToString()),
                $"Node inspection failed with exit code {process.ExitCode}.{Environment.NewLine}{stdout}{stderr}");
        }

        if (!File.Exists(outputPath))
        {
            return ResourceNodeInspectionResult.Failure($"Node inspection did not create output JSON.{Environment.NewLine}{stdout}{stderr}");
        }

        var nodes = JsonSerializer.Deserialize<List<ResourceNodeViewModel>>(await File.ReadAllTextAsync(outputPath, cancellationToken), JsonOptions) ?? [];
        _coordinateConverter.Calibrate(nodes);
        foreach (var node in nodes)
        {
            var point = _coordinateConverter.WorldToMap(node.WorldX, node.WorldY);
            node.MapX = point.X;
            node.MapY = point.Y;
        }

        TryDelete(outputPath);

        var log = new StringBuilder();
        log.AppendLine($"Extracted {nodes.Count} resource nodes.");
        log.AppendLine($"Node breakdown: ordinary={nodes.Count(node => node.NodeKind.Equals("ResourceNode", StringComparison.OrdinalIgnoreCase))}, wells={nodes.Count(node => node.NodeKind.Equals("Well", StringComparison.OrdinalIgnoreCase))}, well satellites={nodes.Where(node => node.NodeKind.Equals("Well", StringComparison.OrdinalIgnoreCase)).Sum(node => node.Satellites.Count)}, geysers={nodes.Count(node => node.NodeKind.Equals("Geyser", StringComparison.OrdinalIgnoreCase))}.");
        log.AppendLine($"Resources: {FormatNodeCounts(nodes.GroupBy(node => node.ResourceType, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase))}");
        log.AppendLine($"Purities: {FormatNodeCounts(nodes.GroupBy(node => node.Purity, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase))}");
        log.AppendLine(_coordinateConverter.LastCalibrationLog);
        log.Append(stdout);
        log.Append(stderr);

        var ordinaryNodes = nodes
            .Where(node => node.NodeKind.Equals("ResourceNode", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var hasMutableResources = ordinaryNodes.Any(node => !node.ResourceType.Equals("Unknown", StringComparison.OrdinalIgnoreCase));
        var hasMutablePurities = ordinaryNodes.Any(node => !node.Purity.Equals("Unknown", StringComparison.OrdinalIgnoreCase));

        if (ordinaryNodes.Length == 0 || !hasMutableResources || !hasMutablePurities)
        {
            var warning = BuildMutationCapabilityWarning(ordinaryNodes.Length, hasMutableResources, hasMutablePurities);
            log.AppendLine();
            log.AppendLine(warning);
            return new ResourceNodeInspectionResult(false, Array.Empty<ResourceNodeViewModel>(), log.ToString().Trim(), warning);
        }

        return new ResourceNodeInspectionResult(true, nodes, log.ToString().Trim(), null);
    }

    private static string BuildMutationCapabilityWarning(int ordinaryNodeCount, bool hasMutableResources, bool hasMutablePurities)
    {
        if (ordinaryNodeCount == 0)
        {
            return "This save does not contain serialized ordinary resource-node actors, so this app cannot safely shuffle or save ordinary node changes. Create or load a save with both resource randomization and purity randomization enabled.";
        }

        if (!hasMutableResources && !hasMutablePurities)
        {
            return "This save uses default resources and default purities. Satisfactory did not serialize mutable resource or purity overrides for ordinary nodes, so this app cannot safely shuffle or save node changes. Create or load a save with both resource randomization and purity randomization enabled.";
        }

        if (!hasMutableResources)
        {
            return "This save has randomized purities but default resources. Purity overrides are serialized, but resource overrides are missing, so this app cannot safely run a full resource shuffle. Enable resource randomization for the save before using this tool.";
        }

        return "This save has randomized resources but default purities. Resource overrides are serialized, but purity overrides are missing, so this app cannot safely save purity changes. Enable purity randomization for the save before using this tool.";
    }

    private static string BuildNodeInspectionFailureMessage(int exitCode, string stdout, string stderr)
    {
        var output = $"{stdout}{Environment.NewLine}{stderr}";
        if (output.Contains("Offset is outside the bounds of the DataView", StringComparison.OrdinalIgnoreCase))
        {
            return "This save could not be parsed safely. The file appears to have a save-body layout that the current parser cannot read, or it may have been externally renamed, edited, or incompletely written. Try loading it in Satisfactory and saving it again, then load that fresh save here.";
        }

        return $"Could not inspect this save. The save worker stopped with exit code {exitCode}, and no changes were written.";
    }

    private static string FormatNodeCounts(IReadOnlyDictionary<string, int> counts) =>
        string.Join(", ", counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"{pair.Key}={pair.Value}"));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Temporary inspection JSON can be cleaned up later if the OS still has it open.
        }
    }
}

public sealed record ResourceNodeInspectionResult(
    bool Success,
    IReadOnlyList<ResourceNodeViewModel> Nodes,
    string Log,
    string? ErrorMessage)
{
    public static ResourceNodeInspectionResult Failure(string message) => new(false, Array.Empty<ResourceNodeViewModel>(), message, message);

    public static ResourceNodeInspectionResult Failure(string message, string log) => new(false, Array.Empty<ResourceNodeViewModel>(), log, message);
}
