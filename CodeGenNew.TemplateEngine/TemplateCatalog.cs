using System.Text.RegularExpressions;
using CodeGenNew.Core;

namespace CodeGenNew.TemplateEngine;

/// <summary> Discovers .tt files in the Templates directory and builds menu-ready TemplateInfo entries
/// (Docs/specs.md section 8). Rebuilt fresh every time -- no caching, so editing a .tt file takes effect
/// on the very next lookup.
///
/// Versioning: a shipped template's file name ends in "_vN" (SP_Save_v1.tt). Only the highest version of each
/// template is offered (Discover); older ones stay on disk but are hidden. An unversioned file name counts as
/// version 0, so a shipped "_v1" supersedes an older install's unversioned copy. </summary>
public static partial class TemplateCatalog
{
    [GeneratedRegex(@"^(?<base>.+)_v(?<ver>\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionSuffix();

    /// <summary> Splits "SP_Save_v2" into ("SP_Save", 2, true); "SP_Save" into ("SP_Save", 0, false). </summary>
    public static (string BaseName, int Version, bool HasVersionSuffix) ParseName(string fileStem)
    {
        var match = VersionSuffix().Match(fileStem);
        if (match.Success && int.TryParse(match.Groups["ver"].Value, out int version))
            return (match.Groups["base"].Value, version, true);
        return (fileStem, 0, false);
    }

    /// <summary> Every .tt file, old versions included (each flagged via SupersededByVersion) -- for the
    /// Template Management screen, which must still let the developer see and delete old versions. </summary>
    public static List<TemplateInfo> DiscoverAll(string templatesDirectory)
    {
        if (!Directory.Exists(templatesDirectory))
            return [];

        var parsed = Directory.GetFiles(templatesDirectory, "*.tt")
            .Select(path =>
            {
                string stem = Path.GetFileNameWithoutExtension(path);
                var (baseName, version, _) = ParseName(stem);
                return (Path: path, BaseName: baseName, Version: version);
            })
            .ToList();

        var latestByName = parsed
            .GroupBy(p => p.BaseName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Max(p => p.Version), StringComparer.OrdinalIgnoreCase);

        var templates = new List<TemplateInfo>();
        foreach (var (path, baseName, version) in parsed)
        {
            int latest = latestByName[baseName];
            int underscoreIndex = baseName.IndexOf('_');

            templates.Add(new TemplateInfo
            {
                FilePath = path,
                Name = baseName,
                Version = version,
                SupersededByVersion = version < latest ? latest : null,
                SubmenuGroup = underscoreIndex > 0 ? baseName[..underscoreIndex] : null,
                Config = TemplateConfig.Load(path + ".config")
            });
        }

        return templates
            .OrderBy(t => t.SubmenuGroup, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Version)
            .ToList();
    }

    /// <summary> The templates to offer (the right-click menu): the latest version of each, old versions omitted. </summary>
    public static List<TemplateInfo> Discover(string templatesDirectory) =>
        DiscoverAll(templatesDirectory).Where(t => !t.IsSuperseded).ToList();

    /// <summary> "SP_Update.tt" (no version) resolves to the LATEST version of SP_Update, so scripts keep working
    /// as templates are revised. "SP_Update_v1.tt" (with a version) pins that exact file, even if superseded. </summary>
    public static TemplateInfo? FindByName(string templatesDirectory, string templateFileName)
    {
        string stem = Path.GetFileNameWithoutExtension(templateFileName);
        var all = DiscoverAll(templatesDirectory);

        var (baseName, _, hasVersionSuffix) = ParseName(stem);
        if (hasVersionSuffix)
        {
            var pinned = all.FirstOrDefault(t => t.FileStem.EqualsIgnoreCase(stem));
            if (pinned is not null)
                return pinned;
        }

        return all.FirstOrDefault(t => !t.IsSuperseded && t.Name.EqualsIgnoreCase(baseName));
    }
}
