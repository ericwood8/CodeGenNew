using CodeGenNew.App.Services;
using CodeGenNew.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CodeGenNew.App.Views;

public sealed partial class ProjectSettingsDialog : ContentDialog
{
    public ProjectSettingsDialogViewModel ViewModel { get; }

    public ProjectSettingsDialog(ProjectSettingsDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        foreach (var (title, description, rows) in ViewModel.Tabs)
        {
            var panel = new StackPanel { Spacing = 8, Padding = new Thickness(0, 8, 0, 0) };
            panel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 220, 220, 220)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 8, 12, 8),
                Child = new TextBlock
                {
                    Text = description,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
                    TextWrapping = TextWrapping.Wrap
                }
            });
            panel.Children.Add(new ScrollViewer
            {
                MaxHeight = 320,
                Content = new ItemsControl { ItemsSource = rows, ItemTemplate = (DataTemplate)Resources["SettingRowTemplate"] }
            });
            SettingsTabs.TabItems.Add(new TabViewItem { Header = title, IsClosable = false, Content = panel });
        }
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
