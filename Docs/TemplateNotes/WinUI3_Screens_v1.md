# WinUI3_Screens_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: MainWindow.Screens.cs   (see OutputName in WinUI3_Screens_v1.tt.config)

The part of the main window that lists the screens, as the other half of the partial class MainWindow: the menu entries and the switch that shows a
screen. MainWindow.xaml keeps an empty <NavigationView> (no MenuItems) and MainWindow.xaml.cs, which stays hand-written, does

    InitializeComponent();
    AddScreens();
    NavView.SelectedItem = NavView.MenuItems[0];
    ShowPage(FirstScreen);

plus the SelectionChanged handler that calls ShowPage(tag), and owns the `_context` field the pages are created with. Adding a table is a regenerate.
A screen is <Table>ListPage (WinUI3_MasterScreen), created as new <Table>ListPage(_context).
```
