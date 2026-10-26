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
        string anchor = Labels.Words(BlazorNames.TypeName(junction.AnchorRole));
        return $$"""
<Page
    x:Class="{{viewNamespace}}.{{PageName(junctionTable.TableName)}}"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Grid Padding="16" RowSpacing="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="{{JunctionPlan.Heading(junctionTable)}}" Style="{ThemeResource TitleTextBlockStyle}" />

        <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="8">
            <ComboBox x:Name="AnchorBox" Width="320" AutomationProperties.Name="{{anchor}}" PlaceholderText="Choose a {{anchor.ToLowerInvariant()}}" DisplayMemberPath="Name" SelectionChanged="OnAnchorChanged" />
            <Button x:Name="EditButton" Content="Edit links" IsEnabled="False" Click="OnEditClick" />
        </StackPanel>

        <InfoBar x:Name="Problem" Grid.Row="2" Severity="Error" IsOpen="False" IsClosable="False" />
    </Grid>
</Page>
""";
    }

    /// <summary> The code-behind. <paramref name="members"/> are the page's fields and constructor parameters (the context, or the clients), <paramref name="readAnchors"/> the statements that return a
    /// <c>List&lt;&lt;Table&gt;LinksAnchor&gt;</c>, <paramref name="openEditor"/> the statements that show the editor for <c>anchor.Id</c>, and <paramref name="failure"/> the exception filter the reads are guarded with. </summary>
    public static string CodeBehind(TableModel junctionTable, string viewNamespace, IEnumerable<string> usings, string field, string constructorParameter, string assignment, string readAnchors, string openEditor, string failure)
    {
        var junction = junctionTable.Junction!;
        string page = PageName(junctionTable.TableName);
        string anchorType = junction.AnchorCs;
        string anchor = Labels.Words(BlazorNames.TypeName(junction.AnchorRole)).ToLowerInvariant();
        string usingLines = string.Concat(usings.Select(u => $"using {u};\n"));
        return $$"""
{{usingLines}}using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace {{viewNamespace}};

/// <summary> One entry of the {{anchor}} drop-down: the row's key and the text that names it. </summary>
public sealed record {{junctionTable.TableName}}LinksAnchor({{anchorType}} Id, string Name);

// A plain Page: the menu shows it. Pick a {{anchor}} and Edit links opens the two-list editor on it.
public sealed partial class {{page}} : Page
{
    {{field}}

    public {{page}}({{constructorParameter}})
    {
        {{assignment}}
        InitializeComponent();
        Loaded += async (_, _) => await LoadAnchorsAsync();
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

    private void OnAnchorChanged(object sender, SelectionChangedEventArgs e) => EditButton.IsEnabled = AnchorBox.SelectedItem is not null;

    private async void OnEditClick(object sender, RoutedEventArgs e)
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
    }
}
""";
    }
}
