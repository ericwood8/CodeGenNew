namespace CodeGenNew.Core;

/// <summary> One state change of a content table (<c>ContentTables</c>) as a list screen offers it: the API route, the button caption, the words of the failure sentence and the states the button is shown for. </summary>
public sealed record ContentAction(string Route, string Caption, string Gerund, string ShownOperator, string ShownState, string TargetState);

/// <summary> What the screens of a content table add to the ordinary list: Publish, Unpublish and Archive buttons on a row (each shown only when it would change something), a status and a published time that are shown but not edited,
/// and a slug box that may be left blank. The words and the tests of a row's state are here so every front end and its tests agree. </summary>
public static class ContentScreens
{
    public static readonly ContentAction Publish = new("publish", "Publish", "publishing", "!=", ContentWords.Published, ContentWords.Published);
    public static readonly ContentAction Unpublish = new("unpublish", "Unpublish", "unpublishing", "==", ContentWords.Published, ContentWords.Draft);
    public static readonly ContentAction Archive = new("archive", "Archive", "archiving", "!=", ContentWords.Archived, ContentWords.Archived);

    /// <summary> The buttons of a row, in the order they appear. </summary>
    public static readonly IReadOnlyList<ContentAction> Actions = [Publish, Unpublish, Archive];

    /// <summary> The hint under the slug box. </summary>
    public const string SlugHint = "Leave blank to make it from the title";

    public static string FailedText(ContentAction action) => $"Problem while {action.Gerund}!";

    /// <summary> The method a front end's client calls the route by, <c>publish</c> as <c>publish</c> (TypeScript) or <c>PublishAsync</c> (C#). </summary>
    public static string CsMethod(ContentAction action) => TemplateHelpers.UpperFirst(action.Route) + "Async";

    /// <summary> Whether the button is shown for a row, in TypeScript: <c>row.status !== 'published'</c>. </summary>
    public static string ShownTs(ContentShape content, ContentAction action, string row) =>
        $"{row}.{JsonNames.Camel(content.Status.Name)} {action.ShownOperator}= '{action.ShownState}'"
        + (WaitsForApproval(content, action) ? $" && {row}.{JsonNames.Camel(content.Status.Name)} !== '{ContentWords.Pending}'" : "");

    /// <summary> Whether the button is Publish of a table that needs approval: a row that already waits for it is not offered Publish again. </summary>
    private static bool WaitsForApproval(ContentShape? content, ContentAction action) => content is { NeedsApproval: true } && action == Publish;

    /// <summary> Whether the button is shown for a row, in C# on the property <paramref name="property"/> of <paramref name="row"/>. </summary>
    public static string ShownCs(ContentAction action, string row, string property, ContentShape? content = null) =>
        $"{row}.{property} {action.ShownOperator} \"{action.ShownState}\"" + (WaitsForApproval(content, action) ? $" && {row}.{property} != \"{ContentWords.Pending}\"" : "");

    /// <summary> The line before a desktop app's guarded UPDATE that reads the clock, or nothing when the action does not stamp the published time (only the first publish does). </summary>
    public static string NowLine(ContentShape content, ContentAction action) =>
        action == Publish && content.PublishedOn is not null ? $"var now = {content.PublishedOn.CSharpBase()}.Now;" : "";

    /// <summary> The <c>SetProperty</c> calls of the guarded UPDATE a desktop app that reads the database makes for the action, the same change as the API's <c>SetStatusAsync</c>: the new state, and for a publish the
    /// published time when it has none yet. </summary>
    public static string SetCalls(ContentShape content, ContentAction action) =>
        $".SetProperty(x => x.{content.Status.CSharpName()}, \"{action.TargetState}\")"
        + (action == Publish && content.PublishedOn is not null ? $".SetProperty(x => x.{content.PublishedOn.CSharpName()}, x => x.{content.PublishedOn.CSharpName()} ?? now)" : "");

    /// <summary> The statements (no indent) a desktop app that reads the database itself runs before a state change of a table that is also in <c>RowApprovals</c>, or none. Publish asks for approval instead (the row becomes
    /// <c>pending</c>; the sentence of a refusal is shown and nothing more happens, <paramref name="stop"/> is what leaves the method), and any other change first withdraws the request that waits for the row. </summary>
    public static IReadOnlyList<string> ApprovalLines(ContentShape content, ContentAction action, string idExpression, string contextExpression, string stop)
    {
        if (!content.NeedsApproval)
            return [];
        string table = content.Table.TableName;
        if (action != Publish)
            return [$"await {table}Approvals.WithdrawWaitingAsync({contextExpression}, {idExpression});"];
        return
        [
            "try",
            "{",
            $"    await {table}Approvals.RequestAsync({contextExpression}, {idExpression}, null);",
            "}",
            "catch (ApprovalRefused ex)",
            "{",
            "    ErrorMessage = ex.Message;",
            $"    {stop}",
            "}"
        ];
    }

    /// <summary> Whether the guarded UPDATE of the desktop app is replaced by the request for approval (Publish of a table that needs approval). </summary>
    public static bool AsksForApproval(ContentShape content, ContentAction action) => content.NeedsApproval && action == Publish;

    /// <summary> The properties an edit dialog copies from the row a state route answered, so the dialog shows the new state while the rest of the form keeps what the person typed:
    /// <c>status: saved.status, publishedOn: saved.publishedOn</c> (an object literal's members). </summary>
    public static string StateCopyTs(ContentShape content, string saved) =>
        $"{JsonNames.Camel(content.Status.Name)}: {saved}.{JsonNames.Camel(content.Status.Name)}"
        + (content.PublishedOn is null ? "" : $", {JsonNames.Camel(content.PublishedOn.Name)}: {saved}.{JsonNames.Camel(content.PublishedOn.Name)}");

    /// <summary> The same copy as C# statements assigning the properties of <paramref name="target"/> from <paramref name="saved"/>. </summary>
    public static string StateCopyCs(ContentShape content, string target, string saved) =>
        $"{target}.{content.Status.CSharpName()} = {saved}.{content.Status.CSharpName()};"
        + (content.PublishedOn is null ? "" : $" {target}.{content.PublishedOn.CSharpName()} = {saved}.{content.PublishedOn.CSharpName()};");

    /// <summary> The buttons of a desktop edit dialog (WinUI3 and Uno), XAML at the dialog's top level: one per action, shown by the view model's <c>&lt;Caption&gt;Visibility</c>, which is collapsed for a new row. </summary>
    public static string DialogXaml(string indent)
    {
        var o = new System.Text.StringBuilder();
        o.Append('\n').Append(indent).Append("<StackPanel Orientation=\"Horizontal\" Spacing=\"8\">");
        foreach (var action in Actions)
            o.Append('\n').Append(indent).Append($"    <Button Content=\"{action.Caption}\" Click=\"On{action.Caption}Click\" Visibility=\"{{x:Bind ViewModel.{action.Caption}Visibility, Mode=OneWay}}\" />");
        o.Append('\n').Append(indent).Append("</StackPanel>");
        return o.ToString();
    }

    /// <summary> The click handlers of those buttons for the dialog's code-behind (four spaces in), each after a blank line and the last without a line end, so a template can append it to a line. </summary>
    public static string DialogHandlers() =>
        string.Concat(Actions.Select(action => $"\n\n    private async void On{action.Caption}Click(object sender, RoutedEventArgs e) => await ViewModel.{CsMethod(action)}();"));

    /// <summary> The view-model members of a desktop edit dialog's buttons (four spaces in): the visibility of each button for the saved row's state and a method per action that changes the state and
    /// shows the new one in the form, leaving what the person typed. A WinUI3 dialog changes it with the guarded UPDATE of the list screen and reads the row again; a Uno dialog asks the API
    /// (<paramref name="clientExpression"/>) and copies the state from the row it answers. </summary>
    public static string DialogMembers(FormModel form, ContentShape content, string entity, string? clientExpression)
    {
        var o = new System.Text.StringBuilder();
        void L(string s = "") => o.Append(s.Length == 0 ? "" : "    " + s).Append('\n');
        string status = content.Status.CSharpName();
        L("/// <summary> Whether the Publish, Unpublish and Archive buttons show: only for a saved row, and only the ones that would change its state. </summary>");
        foreach (var action in Actions)
            L($"[ObservableProperty]\n    private Microsoft.UI.Xaml.Visibility _{TemplateHelpers.LowerFirst(action.Caption)}Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;\n");
        L("private void RefreshContentButtons()");
        L("{");
        foreach (var action in Actions)
            L($"    {action.Caption}Visibility = _editing is not null && {ShownCs(action, "this", status, content)} ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;");
        L("}");
        string key = content.Key.CSharpName();
        string stateColumns(string row) => string.Concat(form.Columns.Where(c => IsStateColumn(content, c)).Select(c => DesktopFormCode.LoadLines(form, c).Replace("editing.", row + ".")))
            .Replace("            ", "        ");
        foreach (var action in Actions)
        {
            L();
            L($"/// <summary> Changes the saved row's state ({action.Route}) and shows the new state; the other fields keep what was typed. </summary>");
            L($"public async Task {CsMethod(action)}()");
            L("{");
            L("    if (_editing is null)");
            L("        return;");
            L("    ErrorMessage = null;");
            if (clientExpression is null)
            {
                L($"    var id = _editing.{key};");
                foreach (string line in ApprovalLines(content, action, "id", "_context", "return;"))
                    L("    " + line);
                if (!AsksForApproval(content, action))
                {
                    if (NowLine(content, action) is { Length: > 0 } now)
                        L("    " + now);
                    L($"    await _context.Set<{entity}>().Where(x => x.{key} == id)");
                    L($"        .ExecuteUpdateAsync(s => s{SetCalls(content, action)});");
                }
                L("    await _context.Entry(_editing).ReloadAsync();   // the update bypassed the tracker, so the row is read again");
                o.Append(stateColumns("_editing"));
            }
            else
            {
                L($"    {entity} saved;");
                L("    try");
                L("    {");
                L($"        saved = await {clientExpression}.{CsMethod(action)}(_editing.{key});");
                L("    }");
                L("    catch (ApiException ex)");
                L("    {");
                L($"        ErrorMessage = ex.Explain(\"{action.Gerund}\");");
                L("        return;");
                L("    }");
                L("    catch (HttpRequestException)");
                L("    {");
                L("        ErrorMessage = \"The server could not be reached.\";");
                L("        return;");
                L("    }");
                L("    " + StateCopyCs(content, "_editing", "saved"));
                o.Append(stateColumns("saved"));
            }
            L("    RefreshContentButtons();");
            L("}");
        }
        return o.Append('\n').ToString();
    }

    private static bool IsStateColumn(ContentShape content, ColumnModel column) =>
        content.Status.Name.EqualsIgnoreCase(column.Name) || (content.PublishedOn is not null && content.PublishedOn.Name.EqualsIgnoreCase(column.Name));

    /// <summary> Whether the column is the slug of the content table, which a person may leave blank (the server makes it). </summary>
    public static bool IsSlug(TableModel table, ProjectSettings project, ColumnModel column) =>
        TableShapes.For(table, project).Content is { } content && content.Slug.Name.EqualsIgnoreCase(column.Name);

    /// <summary> Whether the column is the status or the published time of the content table, which the routes own: the form shows them as text. </summary>
    public static bool IsStateColumn(TableModel table, ProjectSettings project, ColumnModel column) =>
        TableShapes.For(table, project).Content is { } content
        && (content.Status.Name.EqualsIgnoreCase(column.Name) || (content.PublishedOn is not null && content.PublishedOn.Name.EqualsIgnoreCase(column.Name)));
}
