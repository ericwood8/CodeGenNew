using System.Data;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> The XAML of one field of a desktop dialog (WinUI3 and Uno, plain and master-detail): the control <see cref="FormModel.KindOf"/> and <see cref="FormModel.ValueOf"/> choose for the column, bound
/// to the view model's property of the column's name. Every dialog writes the same text from here. A guarded field is wrapped by the template around this. Twelve spaces in, whole lines. </summary>
public static class DesktopFormXaml
{
    /// <summary> <paramref name="withGuards"/>: a guarded column (<c>GuardedColumns</c>) is wrapped in a panel that is enabled only when the view model's <c>&lt;Column&gt;Allowed</c> is true, with the guard's sentence
    /// under it when not (the Uno dialogs read the API's guards; the WinUI3 ones have none). </summary>
    public static string Field(FormModel form, ProjectSettings project, TableModel table, ColumnModel column, bool textKey, bool withGuards = false)
    {
        var guard = withGuards && form.KindOf(column) != FieldKind.SystemStamp ? form.GuardOf(column) : null;
        if (guard is null)
            return Control(form, project, table, column, textKey);
        // a guarded column is a control only for a person who may change it (the API's guards answer); everyone else sees it disabled and why
        return $"            <StackPanel IsEnabled=\"{{x:Bind ViewModel.{column.Name}Allowed, Mode=OneWay}}\">\n"
            + Control(form, project, table, column, textKey)
            + "            </StackPanel>\n"
            + $"            <TextBlock Text=\"{System.Security.SecurityElement.Escape(guard.Note)}\" TextWrapping=\"Wrap\" FontStyle=\"Italic\" Visibility=\"{{x:Bind ViewModel.{column.Name}NoteVisibility, Mode=OneWay}}\" />\n";
    }

    private static string Control(FormModel form, ProjectSettings project, TableModel table, ColumnModel column, bool textKey)
    {
        var kind = form.KindOf(column);
        var value = form.ValueOf(column);
        string prop = column.Name;
        string label = Labels.Column(column.Name);
        string required = RequiredMark.XamlAttributes(form.IsRequired(column), form.IsMarked(column));
        var o = new StringBuilder();
        void L(string s) => o.Append(s).Append('\n');
        const string I = "            ";

        if (kind == FieldKind.SystemStamp)
        {
            // who made the row and when it changed: the system writes it, so it is text, not a box
            L(I + "<StackPanel Spacing=\"2\">");
            L(I + $"    <TextBlock Text=\"{label}\" FontWeight=\"SemiBold\" />");
            L(I + $"    <TextBlock Text=\"{{x:Bind ViewModel.{prop}, Mode=OneWay}}\" IsTextSelectionEnabled=\"True\" />");
            L(I + "</StackPanel>");
            return o.ToString();
        }

        if (kind == FieldKind.Lookup)
        {
            var p = form.ParentOf(column)!;
            if (p.TypeAhead)
            {
                L(I + "<AutoSuggestBox" + (form.IsMarked(column) ? "" : " Header=\"" + label + "\"") + RequiredMark.XamlAttributes(form.IsRequired(column), false)
                    + $" AutomationProperties.Name=\"{label}\" PlaceholderText=\"Type to find\" DisplayMemberPath=\"Text\" TextMemberPath=\"Text\"");
                L(I + $"                Text=\"{{x:Bind ViewModel.{prop}Text, Mode=TwoWay}}\" TextChanged=\"On{prop}TextChanged\" SuggestionChosen=\"On{prop}Chosen\">");
                if (form.IsMarked(column))
                {
                    // an AutoSuggestBox has no header template: the caption and its red asterisk are the header itself
                    L(I + "    <AutoSuggestBox.Header>");
                    L(I + $"        <TextBlock><Run Text=\"{label}\" /><Run Text=\" {RequiredMark.Glyph}\" Foreground=\"Red\" /></TextBlock>");
                    L(I + "    </AutoSuggestBox.Header>");
                }
                L(I + "</AutoSuggestBox>");
            }
            else
            {
                L(I + $"<ComboBox Header=\"{label}\"{required} ItemsSource=\"{{x:Bind ViewModel.{WinUI3Names.OptionsProperty(p)}}}\"");
                L(I + "          SelectedValuePath=\"Id\" DisplayMemberPath=\"Text\"");
                L(I + $"          SelectedValue=\"{{x:Bind ViewModel.{prop}, Mode=TwoWay}}\" />");
            }
        }
        else if (kind == FieldKind.Choices)
        {
            L(I + $"<ComboBox Header=\"{label}\"{required} ItemsSource=\"{{x:Bind ViewModel.{prop}Choices}}\"");
            L(I + $"          SelectedItem=\"{{x:Bind ViewModel.{prop}, Mode=TwoWay}}\" />");
        }
        else if (kind == FieldKind.Money)
        {
            L(I + $"<NumberBox x:Name=\"{prop}Box\" Header=\"{label}\"{required} Value=\"{{x:Bind ViewModel.{prop}, Mode=TwoWay}}\"");
            L(I + $"           {project.MoneyLimitAttributes(table.TableName, column)}ValidationMode=\"InvalidInputOverwritten\" Width=\"180\" HorizontalAlignment=\"Left\" />");
        }
        else if (value == ValueKind.Decimal)
        {
            var (decMin, decMax) = NumericClassifier.DecimalRange(column);
            int decDigits = TemplateHelpers.Inv(decMax).Length + (column.SqlType == SqlDbType.Decimal ? NumericClassifier.DecimalPlaces(column) : 4);
            string decWidth = $" Width=\"{Math.Max(Math.Min(decDigits, 12) * 10 + 150, label.Length * 7 + 8)}\" HorizontalAlignment=\"Left\"";
            L(I + $"<NumberBox x:Name=\"{prop}Box\" Header=\"{label}\"{required} Value=\"{{x:Bind ViewModel.{prop}, Mode=TwoWay}}\"");
            L(I + $"           Minimum=\"{TemplateHelpers.Inv(decMin)}\" Maximum=\"{TemplateHelpers.Inv(decMax)}\" SmallChange=\"1\" LargeChange=\"10\"");
            L(I + $"           SpinButtonPlacementMode=\"Inline\" ValidationMode=\"InvalidInputOverwritten\"{decWidth} />");
        }
        else if (value == ValueKind.WholeNumber)
        {
            var range = project.RangeFor(column)!.Value;
            // As wide as the range's widest number (at most nine digits) plus about 150 pixels for the spin buttons, clear button and padding, and never narrower than the header text.
            int digits = Math.Max(range.Min.ToString().Length, range.Max.ToString().Length);
            string width = $" Width=\"{Math.Max(Math.Min(digits, 9) * 10 + 150, label.Length * 7 + 8)}\" HorizontalAlignment=\"Left\"";
            L(I + $"<NumberBox Header=\"{label}\"{required} Value=\"{{x:Bind ViewModel.{prop}, Mode=TwoWay}}\"");
            L(I + $"           Minimum=\"{range.Min}\" Maximum=\"{range.Max}\" SmallChange=\"1\" LargeChange=\"{(range.Max - range.Min >= 100 ? 10 : 1)}\"");
            L(I + $"           SpinButtonPlacementMode=\"Inline\" ValidationMode=\"InvalidInputOverwritten\"{width} />");
        }
        else if (kind == FieldKind.Boolean)
        {
            L(I + $"<CheckBox Content=\"{label}?\" IsChecked=\"{{x:Bind ViewModel.{prop}, Mode=TwoWay}}\" />");
        }
        else if (kind == FieldKind.Date)
        {
            L(I + "<StackPanel Orientation=\"Horizontal\" Spacing=\"8\">");
            L(I + $"    <CalendarDatePicker Header=\"{label}\"{required} Date=\"{{x:Bind ViewModel.{prop}, Mode=TwoWay}}\"");
            L(I + "                        DateFormat=\"{}{month.integer}/{day.integer}/{year.full}\" PlaceholderText=\"mm/dd/yyyy\" Width=\"180\" />");
            if (column.IsNullable)
                L(I + $"    <Button Content=\"Clear\" VerticalAlignment=\"Bottom\" Click=\"OnClear{prop}Click\" />");
            L(I + "</StackPanel>");
        }
        else if (kind == FieldKind.Multiline)
        {
            L(I + $"<TextBox Header=\"{label}\"{required} Text=\"{{x:Bind ViewModel.{prop}, Mode=TwoWay}}\"");
            L(I + $"         AcceptsReturn=\"True\" TextWrapping=\"Wrap\" Height=\"80\" MaxLength=\"{TemplateHelpers.Chars(column)}\" />");
        }
        else
        {
            string maxLen = TemplateHelpers.IsText(column) && TemplateHelpers.Chars(column) > 0 ? $" MaxLength=\"{TemplateHelpers.Chars(column)}\"" : "";
            // the key of a new row is typed; once the row exists it is read-only (the API does not change a key)
            string readOnly = textKey && column.IsPrimaryKey ? " IsReadOnly=\"{x:Bind ViewModel.IsExisting}\"" : "";
            L(I + $"<TextBox Header=\"{label}\"{required} Text=\"{{x:Bind ViewModel.{prop}, Mode=TwoWay}}\"{maxLen}{readOnly} />");
        }
        return o.ToString();
    }
}
