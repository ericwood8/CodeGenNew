using CodeGenNew.App.Services;
using CodeGenNew.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace CodeGenNew.App.Views;

public sealed partial class ProjectSettingsDialog : ContentDialog
{
    public ProjectSettingsDialogViewModel ViewModel { get; }

    public ProjectSettingsDialog(ProjectSettingsDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ContentDialogUx.Apply(this, primaryAccessKey: "S", secondaryAccessKey: "N", closeAccessKey: "C");
        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private void OnProjectSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectCombo.SelectedItem is string name)
            ViewModel.Load(name);
    }

    // A failed save keeps the dialog open so the message stays visible next to what needs fixing.
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!ViewModel.TrySave())
            args.Cancel = true;
    }
}
