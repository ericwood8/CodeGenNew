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
        string cat = s.Category.CSharpName(), rate = s.Rate.CSharpName(), tax = s.Tax.CSharpName(), key = s.Key.CSharpName();
        string day = s.Day?.CSharpName() ?? "";
        bool dating = s.ValidFrom is not null;
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
        string dayValue = s.Day is not null ? "row." + day : s.ValidFrom is { } from && from.CSharpBase() != "DateOnly" ? "DateTime.Today" : "DateOnly.FromDateTime(DateTime.Now)";
        if (dating)
        {
            string catType = s.ValidFrom!.CSharpBase();
            string asOf = s.Day is null ? dayValue : s.Day.CSharpBase() == catType ? dayValue : catType == "DateOnly" ? $"DateOnly.FromDateTime({dayValue})" : $"{dayValue}.ToDateTime(TimeOnly.MinValue)";
            L($"        var asOf = {asOf};");
        }
        L("        bool keep = false;");
        L("        if (!isNew)");
        L("        {");
        string sameDay = s.Day is null ? "" : s.Day.CSharpBase() == "DateOnly" ? $" && stored.{day} == row.{day}" : $" && stored.{day}.Date == row.{day}.Date";
        L($"            var stored = await context.Set<{entity}>().AsNoTracking().Where(x => x.{key} == row.{key}).Select(x => new {{ x.{cat}, x.{rate}{(s.Day is null ? "" : ", x." + day)} }}).FirstOrDefaultAsync();");
        L($"            if (stored is not null && stored.{cat} == category{sameDay})");
        L("            {");
        L($"                row.{rate} = stored.{rate};");
        L("                keep = true;");
        L("            }");
        L("        }");
        L("        if (!keep)");
        L("        {");
        string dates = dating ? ", x." + s.ValidFrom!.CSharpName() + (s.ValidTo is null ? "" : ", x." + s.ValidTo.CSharpName()) : "";
        L($"            var found = await context.Set<{s.CategoryTable.TableName}>().AsNoTracking().Where(x => x.{s.CategoryKey.CSharpName()} == category).Select(x => new {{ x.{s.CategoryRate.CSharpName()}{dates} }}).FirstOrDefaultAsync();");
        L("            if (found is null)");
        L("                return RowShapeProblems.TaxUnknown($\"" + TaxWords.Unknown("{category}") + "\");");
        if (dating)
        {
            string ended = s.ValidTo is null ? "" : $" || (found.{s.ValidTo.CSharpName()} is {{ }} validTo && validTo < asOf)";
            L($"            if (found.{s.ValidFrom!.CSharpName()} > asOf{ended})");
            L("                return RowShapeProblems.TaxNotInForce($\"" + TaxWords.NotInForce("{category}", "{asOf:yyyy-MM-dd}") + "\");");
        }
        L($"            row.{rate} = ({s.Rate.CSharpBase()})found.{s.CategoryRate.CSharpName()};");
        L("        }");
        L($"        row.{tax} = ({s.Tax.CSharpBase()})Math.Round((decimal)row.{s.Taxable.CSharpName()} * (decimal)row.{rate}, {s.Tax.Scale ?? TaxWords.DefaultScale}, MidpointRounding.AwayFromZero);");
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
