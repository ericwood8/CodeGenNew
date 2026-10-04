using CodeGenNew.App.Services;
using CodeGenNew.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace CodeGenNew.App.Views;

/// <summary> The dialog behind the Generate All button. Generate keeps the dialog open so the list of files and what differs can be read; Close ends it. </summary>
public sealed partial class GenerateAllDialog : ContentDialog
{
    public GenerateAllDialogViewModel ViewModel { get; }
    private readonly Microsoft.UI.Xaml.Window _ownerWindow;

    public GenerateAllDialog(GenerateAllDialogViewModel viewModel, Microsoft.UI.Xaml.Window ownerWindow)
    {
        ViewModel = viewModel;
        _ownerWindow = ownerWindow;
        InitializeComponent();
        ContentDialogUx.Apply(this, primaryAccessKey: "G", closeAccessKey: "C");
        PrimaryButtonClick += OnGenerateClick;
    }

    private void OnBrowseClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var hwnd = WindowNative.GetWindowHandle(_ownerWindow);
        string? start = Directory.Exists(ViewModel.OutputDirectory) ? ViewModel.OutputDirectory : null;
        string? selected = NativeFolderPicker.PickFolder(hwnd, "Select the folder the project's stack folders go under", start);
        if (selected is not null)
            ViewModel.OutputDirectory = selected;
    }

    private async void OnGenerateClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var deferral = args.GetDeferral();
        try
        {
            await ViewModel.GenerateAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }
}
