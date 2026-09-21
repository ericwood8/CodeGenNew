using System.Diagnostics;

namespace CodeGenNew.App.Services;

/// <summary> Finds and launches the developer's preferred text editor rather than
/// relying on Windows' file-association prompt for .tt/.sql files, which may not be set up at all. </summary>
public static class EditorLocator
{
    /// <summary> Probed once, on first run only (AppSettingsService caches the result in Settings.json).
    /// Ordered by preference; the classic Notepad at the end always exists, so this is never null. </summary>
    public static string? FindPreferredEditor()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        string[] candidates =
        [
            Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(programFiles, "Microsoft VS Code", "Code.exe"),
            Path.Combine(programFilesX86, "Microsoft VS Code", "Code.exe"),
            Path.Combine(programFiles, "Notepad++", "notepad++.exe"),
            Path.Combine(programFilesX86, "Notepad++", "notepad++.exe"),
            Path.Combine(programFiles, "Sublime Text", "sublime_text.exe"),
            Path.Combine(programFiles, "Sublime Text 3", "sublime_text.exe"),
            Path.Combine(windows, "notepad.exe"),
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary> Opens a file directly in <paramref name="preferredEditorPath"/> when it still exists;
    /// falls back to Windows' own file-association handling (ShellExecute) otherwise -- e.g. if the
    /// editor was uninstalled since it was detected. </summary>
    public static void OpenFile(string filePath, string? preferredEditorPath)
    {
        if (!string.IsNullOrEmpty(preferredEditorPath) && File.Exists(preferredEditorPath))
        {
            Process.Start(new ProcessStartInfo(preferredEditorPath, $"\"{filePath}\"") { UseShellExecute = true });
            return;
        }

        Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
    }
}
