namespace CodeGenNew.Core;

/// <summary> The page the menu opens for a junction table on WinUI3 and Uno (<see cref="JunctionPlan"/>): a drop-down of the anchor's rows and an Edit links button that opens the two-list editor dialog on the
/// row picked. The XAML is the same for both stacks, and so is the code-behind but for how the anchor rows are read and the editor opened, which the template says. </summary>
public static class DesktopJunctionPage
{
    /// <summary> The class of the page: <c>ItemWarehouseLinksPage</c>. </summary>
    public static string PageName(string junctionTable) => junctionTable + "LinksPage";

    public static string Xaml(TableModel junctionTable, string viewNamespace)
    {
        var junction = junctionTable.Junction!;
        string anchor = junction.AnchorWords;
        return $$"""
<Page
    x:Class="{{viewNamespace}}.{{PageName(junctionTable.TableName)}}"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:{{viewNamespace}}">

    <Grid Padding="16" RowSpacing="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="{{JunctionPlan.Heading(junctionTable)}}" Style="{ThemeResource TitleTextBlockStyle}" />

        <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="8">
            <ComboBox x:Name="AnchorBox" Width="320" AutomationProperties.Name="{{anchor}}" PlaceholderText="Choose a {{anchor.ToLowerInvariant()}}" DisplayMemberPath="Name" SelectionChanged="OnAnchorChanged" />
            <Button x:Name="CreateButton" Content="Create" FontWeight="Bold" Background="{ThemeResource SystemControlBackgroundBaseLowBrush}" IsEnabled="False" Click="OnCreateClick" />
            <Button x:Name="EditButton" Content="Edit links" FontWeight="Bold" Background="{ThemeResource SystemControlBackgroundBaseLowBrush}" IsEnabled="False" Click="OnEditClick" />
        </StackPanel>

        <InfoBar x:Name="Problem" Grid.Row="2" Severity="Error" IsOpen="False" IsClosable="False" />

        <!-- Every existing link, boxed in a line around the grid with a line between each row, like the master grids. -->
        <Border Grid.Row="3" BorderBrush="{ThemeResource ControlStrokeColorSecondaryBrush}" BorderThickness="1" CornerRadius="4" Background="Transparent">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="*" />
                </Grid.RowDefinitions>
                <Border Grid.Row="0" BorderBrush="{ThemeResource ControlStrokeColorSecondaryBrush}" BorderThickness="0,0,0,1" Padding="8">
                    <Grid ColumnSpacing="8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="{{anchor}}" FontWeight="SemiBold" />
                        <TextBlock Grid.Column="1" Text="{{junction.TargetWords}}" FontWeight="SemiBold" />
                    </Grid>
                </Border>
                <ListView Grid.Row="1" ItemsSource="{x:Bind Links}" SelectionMode="None" Padding="0">
                    <ListView.ItemContainerStyle>
                        <Style TargetType="ListViewItem" BasedOn="{StaticResource DefaultListViewItemStyle}">
                            <Setter Property="MinHeight" Value="0" />
                        </Style>
                    </ListView.ItemContainerStyle>
                    <ListView.ItemTemplate>
                        <DataTemplate x:DataType="local:{{junctionTable.TableName}}LinksRow">
                            <Border BorderBrush="{ThemeResource ControlStrokeColorSecondaryBrush}" BorderThickness="0,0,0,1" Padding="8,4">
                                <Grid ColumnSpacing="8">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="*" />
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Grid.Column="0" Text="{x:Bind Anchor}" />
                                    <TextBlock Grid.Column="1" Text="{x:Bind Target}" />
                                </Grid>
                            </Border>
                        </DataTemplate>
                    </ListView.ItemTemplate>
                </ListView>
            </Grid>
        </Border>
    </Grid>
</Page>
""";
    }

    /// <summary> The code-behind. <paramref name="members"/> are the page's fields and constructor parameters (the context, or the clients), <paramref name="readAnchors"/> the statements that return a
    /// <c>List&lt;&lt;Table&gt;LinksAnchor&gt;</c>, <paramref name="openEditor"/> the statements that show the editor for <c>anchor.Id</c>, <paramref name="readHasLinks"/> the statements that return whether <c>anchor.Id</c> has any link, <paramref name="readLinks"/> the statements that return the <c>List&lt;&lt;Table&gt;LinksRow&gt;</c> of every existing link, and <paramref name="failure"/> the exception filter the reads are guarded with. </summary>
    public static string CodeBehind(TableModel junctionTable, string viewNamespace, IEnumerable<string> usings, string field, string constructorParameter, string assignment, string readAnchors, string openEditor,
        string readHasLinks, string readLinks, string failure, string? mayWrite = null)
    {
        string hideEdit = mayWrite is null ? "" : $"\n        CreateButton.Visibility = EditButton.Visibility = {mayWrite} ? Visibility.Visible : Visibility.Collapsed;";
        var junction = junctionTable.Junction!;
        string page = PageName(junctionTable.TableName);
        string anchorType = junction.AnchorCs;
        string targetType = junction.TargetCs;
        string anchor = junction.AnchorWords.ToLowerInvariant();
        string usingLines = string.Concat(usings.Select(u => $"using {u};\n"));
        return $$"""
{{usingLines}}using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace {{viewNamespace}};

/// <summary> One entry of the {{anchor}} drop-down: the row's key and the text that names it. </summary>
public sealed record {{junctionTable.TableName}}LinksAnchor({{anchorType}} Id, string Name);

/// <summary> One existing link, as a row of the grid: the keys and the texts of the two rows. </summary>
public sealed class {{junctionTable.TableName}}LinksRow
{
    public {{anchorType}} AnchorId { get; set; }
    public string? Anchor { get; set; }
    public {{targetType}} TargetId { get; set; }
    public string? Target { get; set; }
}

// A plain Page: the menu shows it. Pick a {{anchor}}: Create opens the two-list editor while it has no links, Edit links once it has.
public sealed partial class {{page}} : Page
{
    {{field}}

    public ObservableCollection<{{junctionTable.TableName}}LinksRow> Links { get; } = [];

    public {{page}}({{constructorParameter}})
    {
        {{assignment}}
        InitializeComponent();{{hideEdit}}
        Loaded += async (_, _) =>
        {
            await LoadAnchorsAsync();
            await LoadLinksAsync();
        };
    }

    private async Task<List<{{junctionTable.TableName}}LinksRow>> ReadLinksAsync()
    {
{{readLinks}}
    }

    private async Task LoadLinksAsync()
    {
        try
        {
            var rows = await ReadLinksAsync();
            Links.Clear();
            foreach (var row in rows)
                Links.Add(row);
        }
        catch ({{failure}})
        {
            Problem.Message = "The links could not be read.";
            Problem.IsOpen = true;
        }
    }

    private async Task LoadAnchorsAsync()
    {
        try
        {
            AnchorBox.ItemsSource = await ReadAnchorsAsync();
        }
        catch ({{failure}})
        {
            Problem.Message = "Failed to load the {{anchor.Pluralize()}}.";
            Problem.IsOpen = true;
        }
    }

    private async Task<List<{{junctionTable.TableName}}LinksAnchor>> ReadAnchorsAsync()
    {
{{readAnchors}}
    }

    private async Task<bool> HasLinksAsync({{junctionTable.TableName}}LinksAnchor anchor)
    {
{{readHasLinks}}
    }

    private async Task ShowButtonsAsync()
    {
        CreateButton.IsEnabled = false;
        EditButton.IsEnabled = false;
        if (AnchorBox.SelectedItem is not {{junctionTable.TableName}}LinksAnchor anchor)
            return;
        try
        {
            bool hasLinks = await HasLinksAsync(anchor);
            CreateButton.IsEnabled = !hasLinks;
            EditButton.IsEnabled = hasLinks;
        }
        catch ({{failure}})
        {
            Problem.Message = "The links could not be read.";
            Problem.IsOpen = true;
        }
    }

    private async void OnAnchorChanged(object sender, SelectionChangedEventArgs e) => await ShowButtonsAsync();

    private async void OnCreateClick(object sender, RoutedEventArgs e) => await OpenEditorAsync();

    private async void OnEditClick(object sender, RoutedEventArgs e) => await OpenEditorAsync();

    private async Task OpenEditorAsync()
    {
        if (AnchorBox.SelectedItem is not {{junctionTable.TableName}}LinksAnchor anchor)
            return;
        Problem.IsOpen = false;
        try
        {
{{openEditor}}
        }
        catch ({{failure}})
        {
            Problem.Message = "The links could not be read.";
            Problem.IsOpen = true;
        }
        await ShowButtonsAsync();
        await LoadLinksAsync();
    }
}
""";
    }
}
