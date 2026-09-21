using CommunityToolkit.Mvvm.ComponentModel;

namespace CodeGenNew.App.ViewModels;

/// <summary> Backs the Location screen (Docs/specs.md section 9.2). </summary>
public partial class LocationDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _outputDirectory = "";

    [ObservableProperty]
    private string _statusMessage = "";

    /// <summary> Validates the path, returning a prompt message if it doesn't exist yet (caller asks the
    /// developer whether to create it) or null if the path is already valid. </summary>
    public string? ValidateAndGetCreatePrompt()
    {
        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            StatusMessage = "An output directory is required.";
            return null;
        }

        if (Directory.Exists(OutputDirectory))
        {
            StatusMessage = "";
            return null;
        }

        return $"'{OutputDirectory}' doesn't exist yet. Create it?";
    }

    public bool TryCreateDirectory()
    {
        try
        {
            Directory.CreateDirectory(OutputDirectory);
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't create that directory: {ex.Message}";
            return false;
        }
    }
}
