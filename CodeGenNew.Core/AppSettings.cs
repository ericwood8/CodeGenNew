namespace CodeGenNew.Core;

/// <summary> Maps directly to Settings.json (Docs/specs.md section 5.1). Password is never part of this -- always re-prompted. </summary>
public class AppSettings
{
    public string OutputDirectory { get; set; } = "Output";
    public string TemplatesDirectory { get; set; } = "Templates";
    public string SpecialLogicColumnsConfigPath { get; set; } = "SpecialLogicColumns.config";
    public string SpCanDeleteVerificationConfigPath { get; set; } = "SpCanDeleteVerification.config";
    public LastConnectionSettings LastConnection { get; set; } = new();

    /// <summary> Full path to the developer's preferred text editor, auto-detected on first run
    /// and used to open template/generated files directly rather than relying on
    /// Windows' file-association prompt for .tt/.sql files. </summary>
    public string PreferredEditorPath { get; set; } = "";
}

public class LastConnectionSettings
{
    public string AuthMode { get; set; } = "SqlLogin"; // "SqlLogin" or "WindowsAuth"
    public string ServerName { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public string UserName { get; set; } = "";
}
