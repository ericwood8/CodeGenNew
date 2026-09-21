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

    public bool AppliesTo(bool tableHasPrimaryKey, bool isView)
    {
        if (Config.RequiresPrimaryKey && !tableHasPrimaryKey)
            return false;
        if (Config.TableOnly && isView)
            return false;
        return true;
    }
}
