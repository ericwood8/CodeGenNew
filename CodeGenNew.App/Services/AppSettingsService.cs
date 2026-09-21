using System.Text.Json;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.App.Services;

/// <summary> Loads/saves Settings.json (Docs/specs.md section 5.1). Password is never part of this -- the
/// Connection dialog always re-prompts for it.
///
/// Settings.json itself lives under %LocalAppData%\CodeGenNew, not next to the exe -- the exe's own
/// folder is a build output directory that gets overwritten by CopyToOutputDirectory on every rebuild
/// (and can differ between how the app was last built/launched, e.g. bin\x64\Debug vs bin\Debug), which
/// was silently wiping the saved last-used connection between runs (Bugs2.txt item 2). Templates/Output/
/// config paths are still resolved relative to the exe's own BaseDirectory, since those do travel with
/// the build output on purpose. </summary>
public class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public string BaseDirectory { get; } = AppContext.BaseDirectory;

    public string SettingsDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodeGenNew");

    public string SettingsPath => Path.Combine(SettingsDirectory, "Settings.json");

    public AppSettings Current { get; private set; }

    public AppSettingsService()
    {
        Current = Load();

        // Silently create Templates\/Output\ if missing and seed default templates/config from the
        // embedded copies baked into this exe -- never overwrites an existing (possibly customized)
        // file (Bugs3.txt items 5-7, 9).
        DefaultAssetSeeder.EnsureDefaultAssets(TemplatesDirectory, SpecialLogicColumnsConfigPath, OutputDirectory, typeof(AppSettingsService).Assembly);

        // Detect the developer's editor once, on first run, and remember it (Bugs3.txt item 13).
        if (string.IsNullOrEmpty(Current.PreferredEditorPath))
        {
            string? detected = EditorLocator.FindPreferredEditor();
            if (detected is not null)
                Update(s => s.PreferredEditorPath = detected);
        }
    }

    public string TemplatesDirectory => Path.Combine(BaseDirectory, Current.TemplatesDirectory);
    public string OutputDirectory => Path.Combine(BaseDirectory, Current.OutputDirectory);
    public string SpecialLogicColumnsConfigPath => Path.Combine(BaseDirectory, Current.SpecialLogicColumnsConfigPath);
    public string SpCanDeleteVerificationConfigPath => Path.Combine(BaseDirectory, Current.SpCanDeleteVerificationConfigPath);

    private AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        string json = File.ReadAllText(SettingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    /// <summary> The way to change and persist a setting: re-reads Settings.json as it is on disk RIGHT NOW, applies
    /// <paramref name="change"/> to that, and writes it back. Saving the whole in-memory copy (Save) would let a
    /// second running instance, or one started before another saved, silently overwrite settings it never touched
    /// -- e.g. an instance that only changed the output folder writing back its stale, blank connection
    /// (Bugs4.txt item 5). Returns null on success, otherwise a message describing why it could not be saved. </summary>
    public string? Update(Action<AppSettings> change)
    {
        try
        {
            var fresh = Load();
            change(fresh);
            Current = fresh;
            Save();
            return null;
        }
        catch (Exception ex)
        {
            return $"Could not save settings to {SettingsPath}: {ex.Message}";
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        string json = JsonSerializer.Serialize(Current, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
