namespace CodeGenNew.Core;

public enum StrategySaveResult
{
    /// <summary> No project of that name existed; the file was written. </summary>
    Saved,
    /// <summary> A project of that name exists and already says everything the choices say; it was left as it is. </summary>
    Unchanged,
    /// <summary> A project of that name exists and says something else; it was not touched. </summary>
    NameTaken
}

/// <summary> Writes the project file the strategy choices stand for. An existing project is never overwritten, because it may hold decisions the choices do not (screens, detail-master tables, enum rules). </summary>
public static class StrategyProjectFile
{
    public const string DefaultName = "MyApp";

    /// <summary> A project name made from a database name or a SQLite file path: its letters, digits and underscores, not starting with a digit. </summary>
    public static string NameFor(string databaseName)
    {
        string last = databaseName.Trim().TrimEnd('\\', '/').Split('\\', '/')[^1];
        string stem = Path.GetFileNameWithoutExtension(last);
        string name = new(stem.Where(c => char.IsAsciiLetterOrDigit(c) || c == '_').ToArray());
        if (name.Length == 0)
            return DefaultName;
        return char.IsAsciiDigit(name[0]) ? "P" + name : name;
    }

    /// <summary> Writes the project unless a file of that name exists; <paramref name="write"/> false (a dry run) only answers what would happen. </summary>
    public static StrategySaveResult Save(string projectsDirectory, string projectName, ProjectSettings settings, bool write = true)
    {
        string path = ProjectSettings.FilePathFor(projectsDirectory, projectName);
        if (File.Exists(path))
        {
            var existing = ProjectSettings.Load(path);
            bool same = settings.Values.All(pair => existing.Values.TryGetValue(pair.Key, out string? value) && value.Equals(pair.Value, StringComparison.OrdinalIgnoreCase));
            return same ? StrategySaveResult.Unchanged : StrategySaveResult.NameTaken;
        }
        if (write)
        {
            Directory.CreateDirectory(projectsDirectory);
            File.WriteAllText(path, settings.ToFileText());
        }
        return StrategySaveResult.Saved;
    }

    /// <summary> An existing project with the technologies of the choices laid over it: its own namespaces, table lists and connection stay, its stacks, access mode and OData switch become the choices'. </summary>
    public static ProjectSettings ChoicesOver(ProjectSettings existing, ProjectSettings fromChoices) =>
        existing.WithOverrides([
            new("Stacks", fromChoices.Values["Stacks"]),
            new("DatabaseProvider", fromChoices.Values["DatabaseProvider"]),
            new("AccessMode", fromChoices.Values.GetValueOrDefault("AccessMode") ?? ""),
            new("OData", fromChoices.Values.GetValueOrDefault("OData") ?? "false")
        ]);
}
