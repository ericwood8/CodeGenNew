namespace CodeGenNew.Core.Tax;

/// <summary> The WinUI3 and Uno parts of the tax subtotals (<c>TaxLines</c> with the document part): a document box, a button and a line of text on the list of the line table, and a read-only line of text under the
/// line grid of a document's master-detail dialog. The words are those of <see cref="TaxScreens"/>. </summary>
public static class TaxDesktopScreens
{
    /// <summary> The box, the button and the answer in the top bar of a WinUI3 or Uno list (sixteen spaces in). </summary>
    public static string Box() =>
        $"                <TextBox Width=\"90\" PlaceholderText=\"Document\" AutomationProperties.Name=\"{TaxScreens.DocumentLabel}\" Text=\"{{x:Bind ViewModel.TaxDocument, Mode=TwoWay}}\" />\n"
        + $"                <Button Content=\"{TaxScreens.ShowCaption}\" FontWeight=\"Bold\" Background=\"{{ThemeResource SystemControlBackgroundBaseLowBrush}}\" Click=\"OnShowTaxClick\" />\n"
        + $"                <TextBlock VerticalAlignment=\"Center\" Text=\"{{x:Bind ViewModel.TaxText, Mode=OneWay}}\" />\n";

    public static string Handlers() =>
        "    private async void OnShowTaxClick(object sender, RoutedEventArgs e) => await ViewModel.ShowTaxAsync();\n";

    /// <summary> The members of the list's view model; <paramref name="read"/> is the body after the document is checked (it sets <c>TaxText</c>). </summary>
    public static string Members(string read) =>
        "    /// <summary> The box and the answer of the tax of one document by category and rate (TaxLines). </summary>\n"
        + "    [ObservableProperty]\n    private string? _taxDocument;\n\n"
        + "    [ObservableProperty]\n    private string? _taxText;\n\n"
        + "    public async Task ShowTaxAsync()\n"
        + "    {\n"
        + "        string document = (TaxDocument ?? \"\").Trim();\n"
        + $"        if (document.Length == 0) {{ TaxText = \"{TaxScreens.NeedsText}\"; return; }}\n"
        + read
        + "    }\n\n";

    private static string Format(string target) =>
        $"        {target} = groups.Count == 0 ? \"{TaxScreens.NoneText}\" : string.Join(\"; \", groups.Select(g => $\"{{g.Label}}: {{g.Rate * 100:0.##}}% on {{g.Taxable}} = {{g.Tax}}\"));\n";

    /// <summary> The Uno read: the API's route. </summary>
    public static string UnoRead() =>
        "        try\n"
        + "        {\n"
        + $"            var rows = await Client.{TaxScreens.CsMethod}(document);\n"
        + "            var groups = rows.Select(r => new { Label = r.Label ?? r.Category?.ToString() ?? \"\", r.Rate, r.Taxable, r.Tax }).ToList();\n"
        + "    " + Format("TaxText")
        + "        }\n"
        + $"        catch (Exception) {{ TaxText = \"{TaxScreens.FailText}\"; }}\n";

    /// <summary> The lines of C# that read the lines of the document <paramref name="documentExpression"/> through <paramref name="context"/> and leave <c>groups</c>. </summary>
    private static string Groups(TaxLineShape s, string context, string documentExpression)
    {
        string row = s.Table.TableName, cat = s.Category.CSharpName(), rate = s.Rate.CSharpName(), key = s.CategoryKey.CSharpName(), catType = s.Category.CSharpBase();
        var o = new System.Text.StringBuilder();
        void L(string text) => o.Append("        ").Append(text).Append('\n');
        L($"var lines = await {context}.Set<{row}>().AsNoTracking().Where(x => x.{s.Document!.CSharpName()} == {documentExpression}).Select(x => new {{ Category = x.{cat}, Rate = x.{rate}, Taxable = x.{s.Taxable.CSharpName()}, Tax = x.{s.Tax.CSharpName()} }}).ToListAsync();");
        L($"var ids = lines.Select(l => ({catType}?)l.Category).OfType<{catType}>().Distinct().ToList();");
        if (s.Label is null)
            L($"var labels = new Dictionary<{catType}, string?>();");
        else
            L($"var labels = await {context}.Set<{s.CategoryTable.TableName}>().AsNoTracking().Where(c => ids.Contains(c.{key})).Select(c => new {{ Id = c.{key}, Label = c.{s.Label.CSharpName()} }}).ToDictionaryAsync(c => c.Id, c => (string?)c.Label);");
        L("var groups = lines.GroupBy(l => new { l.Category, l.Rate }).Select(g => new");
        L("{");
        L($"    Label = ({catType}?)g.Key.Category is {{ }} id && labels.TryGetValue(id, out var name) ? name : (({catType}?)g.Key.Category)?.ToString() ?? \"\",");
        L("    g.Key.Rate,");
        L("    Taxable = g.Sum(l => (decimal)l.Taxable),");
        L("    Tax = g.Sum(l => (decimal)l.Tax)");
        L("}).OrderBy(g => g.Label).ToList();");
        return o.ToString();
    }

    /// <summary> The WinUI3 read: the same query as the API's route, over the dialog's own context. </summary>
    public static string WinUI3Read(TableModel table, ProjectSettings project, string context)
    {
        if (TableShapes.For(table, project).TaxLine is not { Document: not null } s)
            return "";
        string docType = s.Document.CSharpBase();
        return $"        if (!{docType}.TryParse(document, System.Globalization.CultureInfo.InvariantCulture, out var documentId)) {{ TaxText = \"{TaxScreens.NeedsText}\"; return; }}\n"
            + Groups(s, context, "documentId") + Format("TaxText");
    }

    /// <summary> The tax shape of the child grid <paramref name="childTable"/> of a document's dialog when it is a line table of <c>TaxLines</c> with a document column that is the dialog's foreign key (<paramref name="fkColumn"/>), or null. </summary>
    public static TaxLineShape? ChildShape(string childTable, string fkColumn, ProjectSettings project) =>
        RatePlan.Remembered(project)?.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(childTable)) is { } table
        && TableShapes.For(table, project).TaxLine is { Document: not null } s && s.Document.Name.EqualsIgnoreCase(fkColumn) ? s : null;

    /// <summary> The call that fills the summary after the child rows are loaded (empty when the child is not a tax line table). </summary>
    public static string LoadCall(string childTable, string fkColumn, string member, ProjectSettings project) =>
        ChildShape(childTable, fkColumn, project) is null ? "" : $"        await Load{member}TaxAsync();\n";

    /// <summary> The read-only text under the child grid in the dialog's XAML (twelve spaces in). </summary>
    public static string DialogRow(string childTable, string fkColumn, string member, ProjectSettings project) =>
        ChildShape(childTable, fkColumn, project) is null ? "" : $"\n            <TextBlock TextWrapping=\"Wrap\" Text=\"{{x:Bind ViewModel.{member}TaxSummary, Mode=OneWay}}\" />\n";

    /// <summary> WinUI3: the summary of the document being edited, read through the dialog's own context. <paramref name="keyExpression"/> is the document's key. </summary>
    public static string DialogMembers(string childTable, string fkColumn, string member, ProjectSettings project, string context, string keyExpression) =>
        ChildShape(childTable, fkColumn, project) is not { } s ? ""
        : Summary(member, Groups(s, context, "documentId") + Format($"{member}TaxSummary"), keyExpression);

    /// <summary> Uno: the summary read through the API's route of the line table (<paramref name="clientMember"/>). </summary>
    public static string UnoDialogMembers(string childTable, string fkColumn, string member, ProjectSettings project, string clientMember, string keyExpression) =>
        ChildShape(childTable, fkColumn, project) is null ? ""
        : Summary(member,
            "        try\n"
            + "        {\n"
            + $"            var rows = await {clientMember}.{TaxScreens.CsMethod}(documentId.ToString()!);\n"
            + "            var groups = rows.Select(r => new { Label = r.Label ?? r.Category?.ToString() ?? \"\", r.Rate, r.Taxable, r.Tax }).ToList();\n"
            + "    " + Format($"{member}TaxSummary")
            + "        }\n"
            + $"        catch (Exception) {{ {member}TaxSummary = \"\"; }}\n", keyExpression);

    private static string Summary(string member, string body, string keyExpression) =>
        "    /// <summary> The tax of the document being edited by category and rate, read-only (TaxLines). </summary>\n"
        + $"    [ObservableProperty]\n    private string? _{char.ToLowerInvariant(member[0])}{member[1..]}TaxSummary;\n\n"
        + $"    private async Task Load{member}TaxAsync()\n"
        + "    {\n"
        + "        if (_editing is null)\n            return;\n"
        + $"        var documentId = {keyExpression};\n"
        + body
        + "    }\n\n";
}
