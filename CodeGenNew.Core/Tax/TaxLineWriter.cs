namespace CodeGenNew.Core.Tax;

/// <summary> The ASP.NET part of <c>TaxLines</c> that <see cref="RowShapeWriter"/> writes into a line table's shape class: <c>CheckTaxAsync</c> and, when the entry names the document column,
/// <c>TaxSubtotalsAsync</c>. </summary>
public static class TaxLineWriter
{
    private static string Zero(ColumnModel column) => column.CSharpBase() switch { "decimal" => "0m", "float" => "0f", _ => "0.0" };

    private static bool IsDateTime(ColumnModel column) => column.CSharpBase() != "DateOnly";

    /// <summary> <c>CheckTaxAsync</c>: the rate of the category is copied when the row is created or its category (or day) changes, kept otherwise; the tax is the taxable amount times the rate over 100. </summary>
    public static string Check(TaxLineShape s, string context, string entity)
    {
        string cat = s.Category.CSharpName(), rate = s.Rate.CSharpName(), tax = s.Tax.CSharpName();
        var o = new System.Text.StringBuilder();
        void L(string text) => o.Append(text).Append('\n');
        L("    /// <summary> The line carries the rate of its category: copied when the row is created or its category" + (s.Day is null ? "" : " or day") + " changes, kept otherwise. The tax is the taxable amount times the rate divided by 100. </summary>");
        L($"    public static async Task<IResult?> CheckTaxAsync({context} context, {entity} row, bool isNew)");
        L("    {");
        L($"        var category = row.{cat};");
        if (s.Category.IsNullable)
        {
            L("        if (category is null)");
            L("        {");
            L($"            row.{rate} = {Zero(s.Rate)};");
            L($"            row.{tax} = {Zero(s.Tax)};");
            L("            return null;");
            L("        }");
        }
        var names = new TaxRateNames("row", "category", "asOf", "keep", "stored", "found", "validTo", "!isNew", "context", entity);
        foreach (string line in TaxRateBlock.Lines(s, names, (condition, notInForce, message) => [condition, $"    return RowShapeProblems.{(notInForce ? "TaxNotInForce" : "TaxUnknown")}($\"{message}\");"]))
            L("        " + line);
        L("        return null;");
        L("    }");
        L("");
        return o.ToString();
    }

    /// <summary> <c>TaxSubtotalsAsync</c>: one row per category and rate of the lines of a document (taxable amount, tax and the number of lines), worked out from the lines on every read. Empty without a document column. </summary>
    public static string Subtotals(TaxLineShape s, string context, string entity)
    {
        if (s.Document is null)
            return "";
        string cat = s.Category.CSharpName(), rate = s.Rate.CSharpName(), key = s.CategoryKey.CSharpName();
        string catType = s.Category.CSharpBase();
        var o = new System.Text.StringBuilder();
        void L(string text) => o.Append(text).Append('\n');
        L("    /// <summary> The tax of one document by category and rate: taxable amount, tax and number of lines, worked out from its lines. </summary>");
        L($"    public static async Task<IResult> TaxSubtotalsAsync({context} context, {s.Document.CSharpBase()}? document)");
        L("    {");
        L("        if (document is null)");
        L("            return Results.Problem(statusCode: 400, title: \"" + TaxWords.SubtotalsNeedsDocument + "\");");
        L($"        var rows = await context.Set<{entity}>().AsNoTracking().Where(x => x.{s.Document.CSharpName()} == document).Select(x => new {{ Category = x.{cat}, Rate = x.{rate}, Taxable = x.{s.Taxable.CSharpName()}, Tax = x.{s.Tax.CSharpName()} }}).ToListAsync();");
        L($"        var ids = rows.Select(r => ({catType}?)r.Category).OfType<{catType}>().Distinct().ToList();");
        if (s.Label is null)
            L($"        var labels = new Dictionary<{catType}, string?>();");
        else
            L($"        var labels = await context.Set<{s.CategoryTable.TableName}>().AsNoTracking().Where(c => ids.Contains(c.{key})).Select(c => new {{ Id = c.{key}, Label = c.{s.Label.CSharpName()} }}).ToDictionaryAsync(c => c.Id, c => (string?)c.Label);");
        L("        var subtotals = rows.GroupBy(r => new { r.Category, r.Rate }).Select(g => new");
        L("        {");
        L("            Category = g.Key.Category,");
        L("            Label = g.Key.Category is { } id && labels.TryGetValue(id, out var label) ? label : null,");
        L("            Rate = g.Key.Rate,");
        L("            Taxable = g.Sum(r => (decimal)r.Taxable),");
        L("            Tax = g.Sum(r => (decimal)r.Tax),");
        L("            Lines = g.Count()");
        L("        }).OrderBy(x => x.Category).ThenBy(x => x.Rate).ToList();");
        L("        return Results.Ok(subtotals);");
        L("    }");
        L("");
        return o.ToString();
    }
}
