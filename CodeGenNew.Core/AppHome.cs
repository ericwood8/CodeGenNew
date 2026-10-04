namespace CodeGenNew.Core;

/// <summary> The folder that holds Settings.json, Templates, Projects and Output. Next to the program when it runs from a build or a plain copy; in the user's own application-data folder
/// (<c>%APPDATA%\CodeGenNew</c>) when it runs as an installed .NET tool, because a tool's folder inside the package store is replaced on every update. <c>CODEGENNEW_HOME</c> overrides both. </summary>
public static class AppHome
{
    public const string EnvironmentVariable = "CODEGENNEW_HOME";

    public static string Resolve(string baseDirectory) =>
        Resolve(baseDirectory, Environment.GetEnvironmentVariable(EnvironmentVariable), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    public static string Resolve(string baseDirectory, string? overridePath, string applicationData)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return Path.GetFullPath(overridePath);
        return IsInstalledTool(baseDirectory) ? Path.Combine(applicationData, "CodeGenNew") : baseDirectory;
    }

    /// <summary> A tool installed with <c>dotnet tool install</c> runs from a <c>.store</c> folder (global: <c>~/.dotnet/tools/.store/...</c>, or <c>&lt;tool-path&gt;/.store/...</c>). </summary>
    public static bool IsInstalledTool(string baseDirectory) =>
        baseDirectory.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Contains(".store", StringComparer.OrdinalIgnoreCase);
}
