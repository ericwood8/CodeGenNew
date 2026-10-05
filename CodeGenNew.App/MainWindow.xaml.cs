using CodeGenNew.App.Services;
using CodeGenNew.App.ViewModels;
using CodeGenNew.App.Views;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CodeGenNew.App;

public sealed partial class MainWindow : Window
{
    private readonly AppSettingsService _settingsService = new();
    private static readonly SolidColorBrush ProblemsBrush = new(Colors.Red);
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        ViewModel = new MainViewModel(_settingsService);
        InitializeComponent();
        Title = "CodeGenNew";
    }

    // Cursor focus starts on the top menu's first button.
    private void OnCommandBarLoaded(object sender, RoutedEventArgs e) =>
        FirstCommandBarButton.Focus(FocusState.Programmatic);

    // AppBarButton's own default ControlTemplate hardcodes its label TextBlock to FontSize="12" -- confirmed
    // straight from the WindowsAppSDK's own generic.xaml, it is a literal value baked into the template's XAML,
    // not bound to the button's own FontSize property at all (unlike the icon area, which does pick it up).
    // Setting FontSize on the AppBarButton itself is therefore a silent no-op for the label text, and there is
    // no style-setter way to override a literal value inside a template without replacing the whole (large,
    // WindowsAppSDK-owned) template. This instead reaches into the already-applied template's visual tree for
    // the TextBlock named "TextLabel" once each button has loaded, and applies the button's own FontSize to it
    // directly -- keeping the XAML's FontSize="32" the single place that number is set.
    private void OnAppBarButtonLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is AppBarButton { FontSize: var fontSize } button && FindDescendant<TextBlock>(button, "TextLabel") is { } label)
            label.FontSize = fontSize;
    }

    private static T? FindDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && match.Name == name)
                return match;
            if (FindDescendant<T>(child, name) is { } found)
                return found;
        }
        return null;
    }

    private async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        var dialogViewModel = new ConnectionDialogViewModel();
        dialogViewModel.LoadFromSettings(_settingsService.Current.LastConnection);

        var dialog = new ConnectionDialog(dialogViewModel) { XamlRoot = Content.XamlRoot };
        var result = await dialog.ShowAsync();

        // Remember a connection that has been shown to work even if the dialog is then cancelled (a successful Test
        // followed by Cancel used to forget it), not only when Save is pressed.
        string? saveError = null;
        if (dialogViewModel.LastTestSucceeded)
            saveError = _settingsService.Update(s => dialogViewModel.SaveToSettings(s.LastConnection));

        if (result == ContentDialogResult.Primary && dialogViewModel.LastTestSucceeded)
            await ViewModel.ConnectAsync(dialogViewModel.BuildRequest());

        if (saveError is not null)
            ViewModel.StatusMessage = saveError; // must not be hidden by the "Loaded N tables" message above
    }

    private async void OnLocationClick(object sender, RoutedEventArgs e)
    {
        var dialogViewModel = new LocationDialogViewModel { OutputDirectory = _settingsService.OutputDirectory };
        var dialog = new LocationDialog(dialogViewModel, this) { XamlRoot = Content.XamlRoot };
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            string? saveError = _settingsService.Update(s => s.OutputDirectory = dialogViewModel.OutputDirectory);
            if (saveError is not null)
                ViewModel.StatusMessage = saveError;
        }
    }

    private async void OnProjectSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialogViewModel = new ProjectSettingsDialogViewModel(_settingsService.ProjectsDirectory, _settingsService.Current.LastProject);
        var dialog = new ProjectSettingsDialog(dialogViewModel) { XamlRoot = Content.XamlRoot };
        var result = await dialog.ShowAsync();

        string? chosen = result switch
        {
            ContentDialogResult.Primary => dialogViewModel.ProjectName.Trim(),
            ContentDialogResult.Secondary => "",
            _ => null
        };
        if (chosen is null)
            return;

        string? saveError = _settingsService.Update(s => s.LastProject = chosen);
        ViewModel.StatusMessage = saveError ?? (chosen.Length == 0
            ? "Generating with each template's own namespaces (no project)."
            : $"Generating with project '{chosen}'.");
    }

    private async void OnEssentialsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string stack })
            return;

        string menuText = TemplateEngine.EssentialsCatalog.Stacks.First(s => s.Stack == stack).MenuText;
        var dialogViewModel = new EssentialsDialogViewModel(stack, menuText, _settingsService.ProjectsDirectory, _settingsService.TemplatesDirectory,
            _settingsService.OutputDirectory, _settingsService.Current.LastProject);
        var dialog = new EssentialsDialog(dialogViewModel, this) { XamlRoot = Content.XamlRoot };
        await dialog.ShowAsync();
    }

    private async void OnGenerateAllClick(object sender, RoutedEventArgs e)
    {
        var dialogViewModel = ViewModel.CreateGenerateAllViewModel(DispatcherQueue);
        if (dialogViewModel is null)
        {
            ViewModel.StatusMessage = "Connect to a database first.";
            return;
        }
        var dialog = new GenerateAllDialog(dialogViewModel, this) { XamlRoot = Content.XamlRoot };
        await dialog.ShowAsync();
    }

    private async void OnManageTemplatesClick(object sender, RoutedEventArgs e)
    {
        var dialogViewModel = new TemplateManagementViewModel(_settingsService);
        var dialog = new TemplateManagementDialog(dialogViewModel) { XamlRoot = Content.XamlRoot };
        await dialog.ShowAsync();
    }

    private void OnTreeSelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (args.AddedItems.Count > 0 && args.AddedItems[0] is TableNodeViewModel table)
            ViewModel.SelectedTable = table;
    }

    private void OnTableRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TableNodeViewModel table)
            return;

        ViewModel.SelectedTable = table;

        var templates = ViewModel.GetApplicableTemplates(table);
        var problems = table.Problems;
        if (templates.Count == 0 && problems.Count == 0)
            return;

        var flyout = new MenuFlyout();

        // A non-selectable header line naming what's off about this table (no key, a reserved-word name, ...) --
        // IsEnabled=false is WinUI's own way to show a label-only, unclickable MenuFlyoutItem. Removed entirely
        // (not left as a blank line) for a table with no problems.
        if (problems.Count > 0)
        {
            var problemsItem = new MenuFlyoutItem { Text = string.Join(", ", problems), IsEnabled = false, Foreground = ProblemsBrush };
            // Setting Foreground above is not enough on its own: MenuFlyoutItem's Disabled visual state sets the
            // rendered TextBlock's Foreground from the ThemeResource "MenuFlyoutItemForegroundDisabled", which wins
            // over the plain Foreground property once IsEnabled=false. Overriding that resource on this one item
            // (a lookup that starts at the element itself before falling back to the app-wide theme) is what
            // actually makes disabled text render red instead of the default greyed-out color.
            problemsItem.Resources["MenuFlyoutItemForegroundDisabled"] = ProblemsBrush;
            flyout.Items.Add(problemsItem);
            if (templates.Count > 0)
                flyout.Items.Add(new MenuFlyoutSeparator());
        }

        AddGrouped(flyout, templates, template => RunTemplateAsync(table, template));

        var position = e.GetPosition((FrameworkElement)sender);
        flyout.ShowAt((FrameworkElement)sender, position);
        e.Handled = true;
    }

    /// <summary> Adds the templates to the menu, one submenu per group (TSX, WinUI3 ...) as the table menu does. </summary>
    private static void AddGrouped(MenuFlyout flyout, IEnumerable<TemplateInfo> templates, Func<TemplateInfo, Task> run)
    {
        var submenus = new Dictionary<string, MenuFlyoutSubItem>();
        foreach (var template in templates)
        {
            var item = new MenuFlyoutItem { Text = template.Name };
            item.Click += async (_, _) => await run(template);

            if (template.SubmenuGroup is not null)
            {
                if (!submenus.TryGetValue(template.SubmenuGroup, out var subItem))
                {
                    subItem = new MenuFlyoutSubItem { Text = template.SubmenuGroup };
                    submenus[template.SubmenuGroup] = subItem;
                    flyout.Items.Add(subItem);
                }
                subItem.Items.Add(item);
            }
            else
            {
                flyout.Items.Add(item);
            }
        }
    }

    /// <summary> The templates that write files for the whole database, grouped by prefix: the toolbar button and a right-click on the database name both show it. </summary>
    private void ShowDatabaseMenu(FrameworkElement anchor, Windows.Foundation.Point? position = null)
    {
        if (!ViewModel.IsConnected)
            return;
        var templates = ViewModel.GetDatabaseTemplates();
        if (templates.Count == 0)
            return;

        var flyout = new MenuFlyout();
        AddGrouped(flyout, templates, async template => await ShowGenerationResultAsync(await ViewModel.RunDatabaseTemplateAsync(template)));
        if (position is { } point)
            flyout.ShowAt(anchor, point);
        else
            flyout.ShowAt(anchor);
    }

    private void OnDatabaseTemplatesClick(object sender, RoutedEventArgs e) => ShowDatabaseMenu((FrameworkElement)sender);

    private void OnDatabaseRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        ShowDatabaseMenu((FrameworkElement)sender, e.GetPosition((FrameworkElement)sender));
        e.Handled = true;
    }

    private async Task RunTemplateAsync(TableNodeViewModel table, TemplateInfo template) =>
        await ShowGenerationResultAsync(await ViewModel.RunTemplateAsync(table, template));

    /// <summary> The dialog after a generation: what was written (or why it failed), with Open File / Copy buttons. </summary>
    private async Task ShowGenerationResultAsync(string? outputFilePath)
    {
        bool failed = outputFilePath is null;
        string message = failed
            ? ViewModel.StatusMessage
            : (ViewModel.LastOutputFiles.Count > 1
                ? $"Wrote {ViewModel.LastOutputFiles.Count} files:\n" + string.Join("\n", ViewModel.LastOutputFiles.Take(12))
                    + (ViewModel.LastOutputFiles.Count > 12 ? "\n..." : "") + "\n\nThe button below opens the first one.\n\n"
                : $"Wrote {outputFilePath}.\n\n")
              + "CodeGenNew never modifies the target database -- review the file(s) and apply them yourself if you're happy with them.";

        // An InfoBar inside the dialog gets the shape-coded severity icon for free (see the winui3 skill)
        // instead of a plain text block, while the ContentDialog itself still carries the OK/Copy/Open-File
        // button flow.
        var infoBar = new InfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Severity = failed ? InfoBarSeverity.Error : InfoBarSeverity.Success,
            Title = failed ? "Generation Failed" : "Done",
            Message = message
        };

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
            Content = infoBar,
            // On success the close button opens the generated file in the developer's editor instead of
            // just dismissing.
            CloseButtonText = failed ? "OK" : "Open File",
            // So the developer can paste the error into a bug report without retyping it.
            SecondaryButtonText = failed ? "Copy" : ""
        };
        ContentDialogUx.Apply(dialog, secondaryAccessKey: failed ? "C" : null, closeAccessKey: "O");

        if (failed)
        {
            dialog.SecondaryButtonClick += (_, _) =>
            {
                var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                package.SetText(message);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            };
        }
        else
        {
            dialog.CloseButtonClick += (_, _) =>
                EditorLocator.OpenFile(outputFilePath!, _settingsService.Current.PreferredEditorPath);
        }

        await dialog.ShowAsync();
    }
}
