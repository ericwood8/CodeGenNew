using CodeGenNew.App.Services;
using CodeGenNew.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace CodeGenNew.App.Views;

public sealed partial class LocationDialog : ContentDialog
{
    public LocationDialogViewModel ViewModel { get; }
    private readonly Microsoft.UI.Xaml.Window _ownerWindow;

    // Tracks whether the developer has already been warned once, this dialog session, that the
    // directory they typed doesn't exist yet -- clicking Save again confirms "yes, create it."
    // Avoids a nested ContentDialog (WinUI only allows one open at a time -- see OnPrimaryButtonClick).
    private string? _pendingCreateDirectory;

    public LocationDialog(LocationDialogViewModel viewModel, Microsoft.UI.Xaml.Window ownerWindow)
    {
        ViewModel = viewModel;
        _ownerWindow = ownerWindow;
        InitializeComponent();
        ContentDialogUx.Apply(this, primaryAccessKey: "S", closeAccessKey: "C");
        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private void OnBrowseClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var hwnd = WindowNative.GetWindowHandle(_ownerWindow);
        string? startDirectory = Directory.Exists(ViewModel.OutputDirectory) ? ViewModel.OutputDirectory : null;

        string? selected = Services.NativeFolderPicker.PickFolder(hwnd, "Select the output directory", startDirectory);
        if (selected is not null)
            ViewModel.OutputDirectory = selected;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.OutputDirectory))
        {
            ViewModel.StatusMessage = "An output directory is required.";
            _pendingCreateDirectory = null;
            args.Cancel = true;
            return;
        }

        string? createPrompt = ViewModel.ValidateAndGetCreatePrompt();
        if (createPrompt is null)
        {
            _pendingCreateDirectory = null;
            return;
        }

        // Second click on the same not-yet-existing path: create it and let the dialog close.
        // A nested confirmation ContentDialog isn't used here because WinUI only allows one
        // ContentDialog open at a time -- stacking a second one on top of this one (which is
        // still open, mid-deferral) throws "Only a single ContentDialog can be open at any time."
        if (_pendingCreateDirectory == ViewModel.OutputDirectory)
        {
            if (!ViewModel.TryCreateDirectory())
            {
                args.Cancel = true;
                return;
            }

            _pendingCreateDirectory = null;
            return;
        }

        ViewModel.StatusMessage = $"'{ViewModel.OutputDirectory}' doesn't exist yet. Click Save again to create it.";
        _pendingCreateDirectory = ViewModel.OutputDirectory;
        args.Cancel = true;
    }
}
