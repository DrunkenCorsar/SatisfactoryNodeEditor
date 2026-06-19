using Microsoft.Win32;
using System.IO;
using System.Text.RegularExpressions;

namespace SatisfactoryNodeEditor.App.Services;

public sealed class SaveFileDialogService
{
    private string? _lastInputDirectory;
    private string? _lastOutputDirectory;

    public string? PickSaveFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Satisfactory save",
            Filter = "Satisfactory saves (*.sav)|*.sav|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = ResolvePreferredInputDirectory()
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        RememberInputPath(dialog.FileName);
        return dialog.FileName;
    }

    public string? PickOutputSaveFile(string suggestedPath)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save edited Satisfactory save as",
            Filter = "Satisfactory saves (*.sav)|*.sav|All files (*.*)|*.*",
            AddExtension = true,
            DefaultExt = ".sav",
            OverwritePrompt = true,
            FileName = string.IsNullOrWhiteSpace(suggestedPath) ? string.Empty : Path.GetFileName(suggestedPath),
            InitialDirectory = ResolveInitialDirectory(suggestedPath)
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        RememberOutputPath(dialog.FileName);
        return dialog.FileName;
    }

    public void RememberInputPath(string path)
    {
        _lastInputDirectory = GetExistingDirectoryFromPath(path) ?? _lastInputDirectory;
    }

    public string? GetPreferredSaveDirectory() =>
        GetExistingDirectory(_lastOutputDirectory)
        ?? GetExistingDirectory(_lastInputDirectory)
        ?? TryGetSatisfactorySaveDirectory();

    private static string? TryGetSatisfactorySaveDirectory()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                return null;
            }

            var saveGamesDirectory = Path.Combine(localAppData, "FactoryGame", "Saved", "SaveGames");
            if (!Directory.Exists(saveGamesDirectory))
            {
                return null;
            }

            return Directory.EnumerateDirectories(saveGamesDirectory)
                .Where(directory => Regex.IsMatch(Path.GetFileName(directory), @"^\d+$"))
                .OrderByDescending(directory => Directory.GetLastWriteTimeUtc(directory))
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private string? ResolveInitialDirectory(string suggestedPath)
    {
        if (!string.IsNullOrWhiteSpace(_lastOutputDirectory) && Directory.Exists(_lastOutputDirectory))
        {
            return _lastOutputDirectory;
        }

        if (!string.IsNullOrWhiteSpace(suggestedPath))
        {
            var directory = Path.GetDirectoryName(suggestedPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                return directory;
            }
        }

        return TryGetSatisfactorySaveDirectory();
    }

    private string? ResolvePreferredInputDirectory() =>
        GetExistingDirectory(_lastInputDirectory) ?? TryGetSatisfactorySaveDirectory();

    private void RememberOutputPath(string path)
    {
        _lastOutputDirectory = GetExistingDirectoryFromPath(path) ?? _lastOutputDirectory;
    }

    private static string? GetExistingDirectoryFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(path);
        return GetExistingDirectory(directory);
    }

    private static string? GetExistingDirectory(string? directory) =>
        !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
            ? directory
            : null;
}
