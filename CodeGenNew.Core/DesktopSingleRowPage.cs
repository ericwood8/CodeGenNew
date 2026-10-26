using System.Data;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> The text of the page a desktop stack (WinUI3, Uno) writes for a table the project keeps one row of (<c>SingleRowTables</c>): the XAML of the form on the page, the number formatters of its boxes
/// and the handlers after the read. The two stacks differ only in how the row is read and which bar shows a failed read, so WinUI3_SingleRowScreen and UNO_SingleRowScreen write the same text from here. Every method
/// returns whole lines ending in a line feed. </summary>
public static class DesktopSingleRowPage
{
    /// <summary> The XAML of the page: the title, the error bar, the form (tabs when the table has more than one kind of field), Save and Reload. <paramref name="withGuards"/> wraps a guarded column as the Uno dialogs do;
    /// <paramref name="loadErrorBar"/> adds a bar for a read that failed (the view model does not exist then); <paramref name="gatedSave"/> shows Save only to the roles that may write (<c>WriteVisibility</c> of the page). </summary>
    public static string Xaml(FormModel form, ProjectSettings project, TableModel table, string viewNamespace, bool withGuards, bool loadErrorBar, bool gatedSave)
    {
        var pages = form.Pages.Select(p => (Header: p.Header.Replace("&", "&amp;"), Columns: p.Columns)).ToList();
        bool tabbed = pages.Count > 1;
        var o = new StringBuilder();
        void L(string s = "") => o.Append(s).Append('\n');

        L("<Page");
        L($"    x:Class=\"{viewNamespace}.{table.TableName}Page\"");
        L("    xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
        L("    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">");
        L();
        L("    <Page.Resources>");
        o.Append(form.Columns.Any(form.IsMarked) ? RequiredMark.XamlHeaderTemplate : "").Append("    </Page.Resources>\n");
        L();
        L("    <Grid Padding=\"16\" RowSpacing=\"12\">");
        L("        <Grid.RowDefinitions>");
        L("            <RowDefinition Height=\"Auto\" />");
        L("            <RowDefinition Height=\"Auto\" />");
        L("            <RowDefinition Height=\"*\" />");
        L("            <RowDefinition Height=\"Auto\" />");
        L("        </Grid.RowDefinitions>");
        L();
        L($"        <TextBlock Grid.Row=\"0\" Text=\"{form.Title}\" Style=\"{{ThemeResource TitleTextBlockStyle}}\" />");
        L();
        if (loadErrorBar)
        {
            L("        <StackPanel Grid.Row=\"1\" Spacing=\"8\">");
            L("            <InfoBar x:Name=\"LoadError\" Severity=\"Error\" IsClosable=\"False\" />");
            L("            <InfoBar Severity=\"Error\" IsOpen=\"{x:Bind ViewModel.HasError, Mode=OneWay}\"");
            L("                     Message=\"{x:Bind ViewModel.ErrorMessage, Mode=OneWay}\" IsClosable=\"False\" />");
            L("        </StackPanel>");
        }
        else
        {
            L("        <InfoBar Grid.Row=\"1\" Severity=\"Error\" IsOpen=\"{x:Bind ViewModel.HasError, Mode=OneWay}\"");
            L("                 Message=\"{x:Bind ViewModel.ErrorMessage, Mode=OneWay}\" IsClosable=\"False\" />");
        }
        L();
        if (tabbed)
        {
            L("        <TabView Grid.Row=\"2\" IsAddTabButtonVisible=\"False\" TabWidthMode=\"SizeToContent\" CanReorderTabs=\"False\" CanDragTabs=\"False\" SelectedIndex=\"0\">");
        }
        else
        {
            L("        <ScrollViewer Grid.Row=\"2\" MaxWidth=\"720\" HorizontalAlignment=\"Left\">");
            L("            <StackPanel Spacing=\"12\" Padding=\"0,0,12,8\">");
        }
        foreach (var page in pages)
        {
            if (tabbed)
            {
                L($"            <TabViewItem Header=\"{page.Header}\" IsClosable=\"False\">");
                L("                <ScrollViewer MaxWidth=\"720\" HorizontalAlignment=\"Left\">");
                L("                    <StackPanel Spacing=\"12\" Padding=\"0,8,12,8\">");
            }
            foreach (var c in page.Columns)
                o.Append(DesktopFormXaml.Field(form, project, table, c, form.TextKey, withGuards));
            if (tabbed)
            {
                L("                    </StackPanel>");
                L("                </ScrollViewer>");
                L("            </TabViewItem>");
            }
        }
        if (tabbed)
        {
            L("        </TabView>");
        }
        else
        {
            L("            </StackPanel>");
            L("        </ScrollViewer>");
        }
        L();
        L("        <StackPanel Grid.Row=\"3\" Orientation=\"Horizontal\" Spacing=\"8\">");
        L($"            <Button Content=\"Save\" Style=\"{{StaticResource AccentButtonStyle}}\" FontWeight=\"Bold\" Click=\"OnSaveClick\"{(gatedSave ? " Visibility=\"{x:Bind WriteVisibility}\"" : "")} />");
        L("            <Button Content=\"Reload\" FontWeight=\"Bold\" Click=\"OnReloadClick\" />");
        L("            <TextBlock x:Name=\"StatusText\" VerticalAlignment=\"Center\" />");
        L("        </StackPanel>");
        L("    </Grid>");
        L("</Page>");
        return o.ToString();
    }

    /// <summary> The constructor lines, after <c>InitializeComponent()</c>, that give the currency and decimal boxes their number formatters (a NumberFormatter cannot be declared in XAML). </summary>
    public static string Formatters(FormModel form, ProjectSettings project)
    {
        var o = new StringBuilder();
        foreach (var c in form.Columns.Where(c => form.ValueOf(c) == ValueKind.Money))
            o.Append($"        {c.Name}Box.NumberFormatter = new CurrencyNumberFormatter(\"{project.CurrencyCode}\", {NumericClassifier.CurrencyDigits(c)});\n");
        foreach (var c in form.Columns.Where(c => form.ValueOf(c) == ValueKind.Decimal && c.SqlType == SqlDbType.Decimal))
        {
            o.Append($"        {c.Name}Box.NumberFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter\n");
            o.Append("        {\n");
            o.Append($"            IntegerDigits = 1, FractionDigits = {NumericClassifier.DecimalPlaces(c)}, IsGrouped = false,\n");
            o.Append($"            NumberRounder = new Windows.Globalization.NumberFormatting.IncrementNumberRounder {{ Increment = {TemplateHelpers.Inv(Math.Pow(10, -NumericClassifier.DecimalPlaces(c)))}, RoundingAlgorithm = Windows.Globalization.NumberFormatting.RoundingAlgorithm.RoundHalfUp }}\n");
            o.Append("        };\n");
        }
        return o.ToString();
    }

    /// <summary> The handlers after the read: Save and Reload, the type-ahead boxes of the lookups that are found by typing, and the Clear button of each nullable date. </summary>
    public static string Handlers(FormModel form, string table)
    {
        var o = new StringBuilder();
        void L(string s = "") => o.Append(s).Append('\n');
        L("    private async void OnSaveClick(object sender, RoutedEventArgs e)");
        L("    {");
        L("        if (ViewModel is null || !await ViewModel.SaveAsync())");
        L("            return;");
        L("        StatusText.Text = \"Saved.\";");
        L("        await ReloadAsync();");
        L("    }");
        L();
        L("    private async void OnReloadClick(object sender, RoutedEventArgs e)");
        L("    {");
        L("        StatusText.Text = \"\";");
        L("        await ReloadAsync();");
        L("    }");
        foreach (var c in form.Columns.Where(c => form.KindOf(c) == FieldKind.Lookup && form.ParentOf(c) is { TypeAhead: true }))
        {
            var p = form.ParentOf(c)!;
            L();
            L($"    // Typing in the {Labels.Column(c.Name)} box takes the chosen row off and lists the rows whose text contains what was typed; picking one chooses it.");
            L($"    private async void On{c.Name}TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)");
            L("    {");
            L("        if (ViewModel is null || args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)");
            L("            return;");
            L($"        ViewModel.{c.Name} = null;");
            L($"        sender.ItemsSource = string.IsNullOrWhiteSpace(sender.Text) ? null : await ViewModel.Suggest{p.BaseName}Async(sender.Text);");
            L("    }");
            L();
            L($"    private void On{c.Name}Chosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)");
            L("    {");
            L("        if (ViewModel is not null)");
            L($"            ViewModel.{c.Name} = (({WinUI3Names.LookupOptionClass(table, "Detail", p.KeyCs)})args.SelectedItem).Id;");
            L("    }");
        }
        foreach (var c in form.Columns.Where(c => form.ValueOf(c) == ValueKind.Date && c.IsNullable))
        {
            L();
            L("    // CalendarDatePicker has no clear button of its own; a nullable date gets one beside it.");
            L($"    private void OnClear{c.Name}Click(object sender, RoutedEventArgs e)");
            L("    {");
            L("        if (ViewModel is not null)");
            L($"            ViewModel.{c.Name} = null;");
            L("    }");
        }
        return o.ToString();
    }
}
