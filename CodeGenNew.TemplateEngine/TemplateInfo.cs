using CodeGenNew.Core;

namespace CodeGenNew.TemplateEngine;

/// <summary> One discovered .tt file (Docs/specs.md section 8). </summary>
public class TemplateInfo
{
    public required string FilePath { get; init; }

    /// <summary> File name without the .tt extension, version suffix included (e.g. "SP_Save_v1"). </summary>
    public string FileStem => Path.GetFileNameWithoutExtension(FilePath);

    /// <summary> File name without the .tt extension and WITHOUT any "_vN" version suffix (e.g. "SP_Save") --
    /// the menu display name and the basis of the generated file's name. </summary>
    public required string Name { get; init; }

    /// <summary> The N of a "_vN" suffix; 0 for an unversioned file name (older installs, hand-made templates). </summary>
    public int Version { get; init; }

    /// <summary> Set when another file with the same Name has a higher Version -- this one is an old version and is
    /// kept on disk (it may be customized) but is not offered in the menu. </summary>
    public int? SupersededByVersion { get; init; }
    public bool IsSuperseded => SupersededByVersion.HasValue;

    /// <summary> Substring before the first underscore in Name, or null if Name has no underscore (flat top-level menu item). </summary>
    public string? SubmenuGroup { get; init; }

    public required TemplateConfig Config { get; init; }

    /// <summary> The name of the file this template writes for one table, e.g. SP_Update.tt + ProductionUnitMaster
    /// -> ProductionUnitMaster_Update.sql. The extension is inferred from SubmenuGroup; unrecognized groups fall
    /// back to .txt. Config.OutputName, when set, overrides the whole name (e.g. "{Table}Api.cs"). </summary>
    public string BuildFileName(string tableName)
    {
        if (!string.IsNullOrWhiteSpace(Config.OutputName))
            return Path.GetFileName(Config.OutputName.Replace("{Table}", tableName, StringComparison.OrdinalIgnoreCase)); // a bare file name, never a path

        string suffix = (SubmenuGroup is not null && Name.Length > SubmenuGroup.Length + 1)
            ? Name[(SubmenuGroup.Length + 1)..]
            : Name;

        string extension = (SubmenuGroup is not null && ExtensionByGroup.TryGetValue(SubmenuGroup, out var ext))
            ? ext
            : "txt";

        return $"{tableName}_{suffix}.{extension}";
    }

    private static readonly Dictionary<string, string> ExtensionByGroup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SP"] = "sql",
        ["CS"] = "cs",
        ["TS"] = "ts",
        ["API"] = "cs",
        ["JS"] = "js",
    };

    public bool AppliesTo(bool tableHasPrimaryKey, bool isView, bool isJunctionTable = false, bool hasChildForeignKeys = false,
        PrimaryKeyShape primaryKeyShape = PrimaryKeyShape.None, bool isNameActiveTable = false)
    {
        if (Config.RequiresPrimaryKey && !tableHasPrimaryKey)
            return false;
        if (Config.TableOnly && isView)
            return false;
        if (Config.RequiresJunctionTable && !isJunctionTable)
            return false;
        if (Config.RequiresChildTables && !hasChildForeignKeys)
            return false;
        if (!Config.PrimaryKeyShapeSatisfies(primaryKeyShape))
            return false;
        if (Config.RequiresNotNameActiveTable && isNameActiveTable)
            return false;
        return true;
    }
}
