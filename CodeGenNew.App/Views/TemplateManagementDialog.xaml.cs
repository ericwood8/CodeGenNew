using CodeGenNew.App.Services;
using CodeGenNew.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace CodeGenNew.App.Views;

public sealed partial class TemplateManagementDialog : ContentDialog
{
    public TemplateManagementViewModel ViewModel { get; }

    public TemplateManagementDialog(TemplateManagementViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ContentDialogUx.Apply(this, primaryAccessKey: "O");
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => ViewModel.Refresh();

    private async void OnNewClick(object sender, RoutedEventArgs e)
    {
        string? name = await PromptForNameAsync((FrameworkElement)sender,
            "Template name (menu prefix uses '_', e.g. SP_Insert):", "");
        if (!string.IsNullOrWhiteSpace(name))
            ViewModel.CreateTemplate(name.Trim());
    }

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TemplateRowViewModel row)
            return;

        string? name = await PromptForNameAsync((FrameworkElement)sender, "New name:", row.Name);
        if (!string.IsNullOrWhiteSpace(name) && name.Trim() != row.Name)
            ViewModel.RenameTemplate(row, name.Trim());
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TemplateRowViewModel row)
            return;

        if (row.ConfirmingDelete)
        {
            ViewModel.DeleteTemplate(row);
            return;
        }

        foreach (var other in ViewModel.Templates)
            other.ConfirmingDelete = false;
        row.ConfirmingDelete = true;
    }

    // Double-clicking a row is a second, more discoverable way to Edit it. Ignored when the
    // double-tap landed on one of the row's own buttons/checkboxes, which have their own meaning.
    private void OnRowDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: TemplateRowViewModel row } source && !IsInsideButton(source))
            ViewModel.OpenInEditor(row);
    }

    private static bool IsInsideButton(DependencyObject element)
    {
        for (var current = element; current is not null; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase) // Button, CheckBox, ...
                return true;
        }
        return false;
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TemplateRowViewModel row)
            ViewModel.OpenInEditor(row);
    }

    /// <summary> Prompts for a name via a Flyout anchored on <paramref name="target"/> rather than a
    /// nested ContentDialog -- this dialog is itself a ContentDialog, and WinUI only allows one
    /// ContentDialog open at a time (stacking a second one throws "Only a single ContentDialog can be
    /// open at any time."). A Flyout is a different popup layer and coexists with it fine. </summary>
    private static Task<string?> PromptForNameAsync(FrameworkElement target, string label, string initialValue)
    {
        var tcs = new TaskCompletionSource<string?>();

        var textBox = new TextBox { Header = label, Text = initialValue, Width = 260 };
        var okButton = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var panel = new StackPanel { Spacing = 4, Padding = new Thickness(4) };
        panel.Children.Add(textBox);
        panel.Children.Add(okButton);

        var flyout = new Flyout { Content = panel };

        okButton.Click += (_, _) =>
        {
            tcs.TrySetResult(textBox.Text);
            flyout.Hide();
        };
        textBox.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                tcs.TrySetResult(textBox.Text);
                flyout.Hide();
            }
        };
        // Fires on any close, including Escape/click-away; a no-op if OK already set a result.
        flyout.Closed += (_, _) => tcs.TrySetResult(null);

        flyout.ShowAt(target);
        textBox.Focus(FocusState.Programmatic);
        textBox.SelectAll();

        return tcs.Task;
    }
}
