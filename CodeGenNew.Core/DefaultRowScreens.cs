namespace CodeGenNew.Core;

/// <summary> What the screens of a default-row table (<c>DefaultRowTables</c>) add to the ordinary list: a "Make default" button on every row that is not the default of its group, which calls the API route that moves
/// the mark and reads the list again. The words are here so every front end agrees. </summary>
public static class DefaultRowScreens
{
    /// <summary> The API route after the key (<c>POST {id}/make-default</c>). </summary>
    public const string RouteName = "make-default";

    /// <summary> The method a TypeScript client calls it by. </summary>
    public const string Route = "makeDefault";

    /// <summary> The method a C# client and a desktop view model call it by. </summary>
    public const string CsMethod = "MakeDefaultAsync";

    public const string Caption = "Make default";

    public const string FailedText = "Problem while making it the default!";

    /// <summary> Shown for the flag of the saved row (the form shows it as text, since only the route moves it). </summary>
    public const string YesText = "Yes";

    public const string NoText = "No";

    /// <summary> The view-model method the dialog's XAML reads the flag through. </summary>
    public const string YesNoMethod = "YesNo";

    /// <summary> Whether the column is the default flag of a default-row table, which the routes own: the form shows it as text and the Make default button moves it. </summary>
    public static bool IsFlagColumn(TableModel table, ProjectSettings project, ColumnModel column) =>
        TableShapes.For(table, project).DefaultRow is { } shape && shape.Flag.Name.EqualsIgnoreCase(column.Name);

    /// <summary> The Make default button of a desktop edit dialog (WinUI3 and Uno), XAML at the dialog's top level: shown by the view model's <c>MakeDefaultVisibility</c>, which is collapsed for a new row and for the default row. </summary>
    public static string DialogXaml(string indent) =>
        $"\n{indent}<Button Content=\"{Caption}\" Click=\"OnMakeDefaultClick\" Visibility=\"{{x:Bind ViewModel.MakeDefaultVisibility, Mode=OneWay}}\" HorizontalAlignment=\"Left\" />";

    /// <summary> The click handler for the dialog's code-behind (four spaces in), after a blank line and without a line end, so a template can append it to a line. </summary>
    public static string DialogHandlers() =>
        $"\n\n    private async void OnMakeDefaultClick(object sender, RoutedEventArgs e) => await ViewModel.{CsMethod}();";

    /// <summary> The line the constructor runs to set the button for the row the dialog opens with. </summary>
    public const string ConstructorLine = "            RefreshDefaultButton();\n";

    /// <summary> The lines a WinUI3 dialog runs before it saves (it writes through its own context, not through the API): a new row is the default when its group has none. </summary>
    public static string CheckLines(ProjectSettings project, TableModel table, string context)
    {
        if (TableShapes.For(table, project).DefaultRow is not { } shape)
            return "";
        string flag = shape.Flag.CSharpName();
        string groups = string.Concat(shape.Groups.Select(g => $" && x.{g.CSharpName()} == entity.{g.CSharpName()}"));
        return $"        if (_editing is null)\n            entity.{flag} = !await {context}.Set<{table.TableName}>().AnyAsync(x => x.{flag}{groups});\n\n";
    }

    /// <summary> The view-model members of the dialog's button (four spaces in): its visibility for the saved row and the method that makes the row the default and shows it in the form. A WinUI3 dialog (<paramref name="clientExpression"/> null)
    /// moves the mark with the same two updates as the list screen, in one transaction; a Uno dialog asks the API. </summary>
    public static string DialogMembers(FormModel form, DefaultRowShape shape, string entity, string? clientExpression)
    {
        var o = new System.Text.StringBuilder();
        void L(string s = "") => o.Append(s.Length == 0 ? "" : "    " + s).Append('\n');
        string flag = shape.Flag.CSharpName(), key = shape.Key.CSharpName();
        L("/// <summary> Whether the Make default button shows: only for a saved row that is not the default of its group. </summary>");
        L("[ObservableProperty]\n    private Microsoft.UI.Xaml.Visibility _makeDefaultVisibility = Microsoft.UI.Xaml.Visibility.Collapsed;\n");
        L($"public string {YesNoMethod}(bool? value) => value == true ? \"{YesText}\" : \"{NoText}\";");
        L();
        L("private void RefreshDefaultButton()");
        L("{");
        L($"    MakeDefaultVisibility = _editing is not null && !_editing.{flag} ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;");
        L("}");
        L();
        L("/// <summary> Makes the saved row the default of its group and shows it; the other fields keep what was typed. </summary>");
        L($"public async Task {CsMethod}()");
        L("{");
        L("    if (_editing is null)");
        L("        return;");
        L("    ErrorMessage = null;");
        if (clientExpression is null)
        {
            string groups = string.Concat(shape.Groups.Select(g => $" && x.{g.CSharpName()} == _editing.{g.CSharpName()}"));
            L($"    var id = _editing.{key};");
            L("    await using var transaction = await _context.Database.BeginTransactionAsync();");
            L($"    await _context.Set<{entity}>().Where(x => x.{key} != id{groups} && x.{flag})");
            L($"        .ExecuteUpdateAsync(s => s.SetProperty(x => x.{flag}, false));");
            L($"    await _context.Set<{entity}>().Where(x => x.{key} == id)");
            L($"        .ExecuteUpdateAsync(s => s.SetProperty(x => x.{flag}, true));");
            L("    await transaction.CommitAsync();");
            L("    await _context.Entry(_editing).ReloadAsync();   // the updates bypassed the tracker, so the row is read again");
            L($"    foreach (var other in _context.ChangeTracker.Entries<{entity}>().Where(x => x.Entity != _editing).ToList())");
            L("        await other.ReloadAsync();   // and so are the other rows of the list that were read before");
        }
        else
        {
            L($"    {entity} saved;");
            L("    try");
            L("    {");
            L($"        saved = await {clientExpression}.{CsMethod}(_editing.{key});");
            L("    }");
            L("    catch (ApiException ex)");
            L("    {");
            L("        ErrorMessage = ex.Explain(\"making it the default\");");
            L("        return;");
            L("    }");
            L("    catch (HttpRequestException)");
            L("    {");
            L("        ErrorMessage = \"The server could not be reached.\";");
            L("        return;");
            L("    }");
            L($"    _editing.{flag} = saved.{flag};");
        }
        L($"    {flag} = _editing.{flag};");
        L("    RefreshDefaultButton();");
        L("}");
        return o.Append('\n').ToString();
    }
}
