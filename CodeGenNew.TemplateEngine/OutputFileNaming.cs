namespace CodeGenNew.TemplateEngine;

/// <summary> Derives the generated file's name from the template name + table name, e.g.
/// SP_Update.tt + ProductionUnitMaster -> ProductionUnitMaster_Update.sql. The extension is inferred
/// from the template's submenu-group prefix; unrecognized prefixes fall back to .txt. A template's .tt.config can override the whole name with OutputName (e.g. "{Table}Api.cs"). </summary>
public static class OutputFileNaming
{
    private static readonly Dictionary<string, string> ExtensionByGroup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SP"] = "sql",
        ["CS"] = "cs",
        ["TS"] = "ts",
        ["API"] = "cs",
        ["JS"] = "js",
    };

    public static string BuildFileName(TemplateInfo template, string tableName)
    {
        if (!string.IsNullOrWhiteSpace(template.Config.OutputName))
            return Path.GetFileName(template.Config.OutputName.Replace("{Table}", tableName, StringComparison.OrdinalIgnoreCase)); // a bare file name, never a path

        string suffix = (template.SubmenuGroup is not null && template.Name.Length > template.SubmenuGroup.Length + 1)
            ? template.Name[(template.SubmenuGroup.Length + 1)..]
            : template.Name;

        string extension = (template.SubmenuGroup is not null && ExtensionByGroup.TryGetValue(template.SubmenuGroup, out var ext))
            ? ext
            : "txt";

        return $"{tableName}_{suffix}.{extension}";
    }
}
