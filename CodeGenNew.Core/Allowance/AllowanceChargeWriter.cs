namespace CodeGenNew.Core.Allowance;

/// <summary> The ASP.NET part of <c>AllowanceCharges</c> that <see cref="RowShapeWriter"/> writes into the table's shape class: <c>CheckAllowanceCharge</c> and, with a document column,
/// <c>AllowanceChargeTotalsAsync</c>. </summary>
/// <summary> The parent table of a table scoped through it, with the C# names of its key and owner and of the column of the row that points at it. </summary>
public sealed record OwnerParent(string Table, string Key, string Owner, string Foreign);

public static class AllowanceChargeWriter
{
    /// <summary> <c>CheckAllowanceCharge</c>: refuses a negative value, a percentage with no base and a row with nothing to work an amount from; otherwise writes the amount (base times percent over 100) or the percentage (amount over base times 100). </summary>
    public static string Check(AllowanceChargeShape s, string entity)
    {
        var o = new System.Text.StringBuilder();
        void L(string text) => o.Append(text).Append('\n');
        L("    /// <summary> The amount is worked out here: from the base and the percentage, or the percentage from the amount and the base. It is stored positive, the flag says whether it is added or taken off. </summary>");
        L($"    public static IResult? CheckAllowanceCharge({entity} row)");
        L("    {");
        var names = new AllowanceChargeNames("row", "baseAmount", "percent", "amount");
        foreach (string line in AllowanceChargeBlock.Lines(s, names, (condition, message) => [condition, $"    return RowShapeProblems.AllowanceInvalid(\"{message}\");"]))
            L("        " + line);
        L("        return null;");
        L("    }");
        L("");
        return o.ToString();
    }

    /// <summary> <c>AllowanceChargeTotalsAsync</c>: the allowances, the charges and the net (charges minus allowances) of one document, worked out from its rows on every read. Empty without a document column. </summary>
    /// <summary> <paramref name="owner"/> is the owner column of a table with its own; <paramref name="parent"/> is the table whose owner decides who owns a row of a table with none (the rows under the parents of the owners the caller may see). </summary>
    public static string Totals(AllowanceChargeShape s, string context, string entity, ColumnModel? owner = null, OwnerParent? parent = null)
    {
        if (s.Document is null)
            return "";
        var o = new System.Text.StringBuilder();
        void L(string text) => o.Append(text).Append('\n');
        L("    /// <summary> The allowances, the charges and the net (charges minus allowances) of one document. </summary>");
        L($"    public static async Task<IResult> AllowanceChargeTotalsAsync({context} context, " + (owner is null && parent is null ? "" : "HttpContext http, ") + $"{s.Document.CSharpBase()}? document" + (s.Line is null ? "" : $", {s.Line.CSharpBase()}? line") + ")");
        L("    {");
        L("        if (document is null)");
        L("            return Results.Problem(statusCode: 400, title: \"" + AllowanceChargeWords.NeedsDocument + "\");");
        L($"        var query = context.Set<{entity}>().AsNoTracking().Where(x => x.{s.Document.CSharpName()} == document);");
        if (s.Line is not null)
        {
            L("        if (line is not null)");
            L($"            query = query.Where(x => x.{s.Line.CSharpName()} == line);");
        }
        if (owner is not null)
        {
            L("        if (await http.RequestServices.GetRequiredService<IOwnerScope>().VisibleOwnersAsync(http.User, http.RequestAborted) is { } owners)");
            L("        {");
            L("            var visible = owners.ToArray();");
            L($"            query = query.Where(x => visible.Contains(x.{owner.CSharpName()}));");
            L("        }");
        }
        if (parent is not null)
        {
            L("        if (await http.RequestServices.GetRequiredService<IOwnerScope>().VisibleOwnersAsync(http.User, http.RequestAborted) is { } parentOwners)");
            L("        {");
            L("            var visible = parentOwners.ToArray();");
            L($"            var documents = context.Set<{parent.Table}>().AsNoTracking().Where(p => visible.Contains(p.{parent.Owner})).Select(p => p.{parent.Key});");
            L($"            query = query.Where(x => documents.Contains(x.{parent.Foreign}));");
            L("        }");
        }
        L($"        var rows = await query.Select(x => new {{ Charge = x.{s.Flag.CSharpName()}, Amount = x.{s.Amount.CSharpName()} }}).ToListAsync();");
        L("        decimal allowances = rows.Where(r => !r.Charge).Sum(r => (decimal)r.Amount);");
        L("        decimal charges = rows.Where(r => r.Charge).Sum(r => (decimal)r.Amount);");
        L("        return Results.Ok(new { Allowances = allowances, Charges = charges, Net = charges - allowances, Rows = rows.Count });");
        L("    }");
        L("");
        return o.ToString();
    }
}
