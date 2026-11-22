namespace CodeGenNew.Core.Tax;

/// <summary> The variable names and the context of one place that writes <see cref="TaxRateBlock"/>: <c>Row</c> is the entity variable, <c>Editing</c> the condition that says the row already exists and
/// <c>Context</c> the expression the database is read through and <c>Entity</c> the entity type of the line table. </summary>
public sealed record TaxRateNames(string Row, string Category, string AsOf, string Keep, string Stored, string Found, string ValidTo, string Editing, string Context, string Entity);

/// <summary> The C# that gives a line of <c>TaxLines</c> its rate and its tax, shared by the API's <c>CheckTaxAsync</c> and the WinUI3 dialog's check: the rate of the category is copied when the row is created or its
/// category or day changes and kept otherwise, a category that does not exist or is not in force is refused, and the tax is the taxable amount times the rate. </summary>
public static class TaxRateBlock
{
    /// <summary> What a caller writes to refuse a row: the line that opens the test (<c>if (...)</c>), whether it is the not-in-force refusal and the interpolated message. </summary>
    public delegate IEnumerable<string> Refusal(string condition, bool notInForce, string message);

    /// <summary> The lines from the day of the rate to the tax, indented from zero. </summary>
    public static IEnumerable<string> Lines(TaxLineShape s, TaxRateNames n, Refusal refuse)
    {
        string cat = s.Category.CSharpName(), rate = s.Rate.CSharpName(), tax = s.Tax.CSharpName(), key = s.Key.CSharpName();
        string day = s.Day?.CSharpName() ?? "";
        bool dating = s.ValidFrom is not null;
        var o = new List<string>();
        if (dating)
        {
            string catType = s.ValidFrom!.CSharpBase();
            string dayValue = s.Day is not null ? $"{n.Row}.{day}" : catType == "DateOnly" ? "DateOnly.FromDateTime(DateTime.Now)" : "DateTime.Today";
            string asOf = s.Day is null || s.Day.CSharpBase() == catType ? dayValue : catType == "DateOnly" ? $"DateOnly.FromDateTime({dayValue})" : $"{dayValue}.ToDateTime(TimeOnly.MinValue)";
            o.Add($"var {n.AsOf} = {asOf};");
        }
        o.Add($"bool {n.Keep} = false;");
        o.Add($"if ({n.Editing})");
        o.Add("{");
        string sameDay = s.Day is null ? "" : s.Day.CSharpBase() == "DateOnly" ? $" && {n.Stored}.{day} == {n.Row}.{day}" : $" && {n.Stored}.{day}.Date == {n.Row}.{day}.Date";
        o.Add($"    var {n.Stored} = await {n.Context}.Set<{n.Entity}>().AsNoTracking().Where(x => x.{key} == {n.Row}.{key}).Select(x => new {{ x.{cat}, x.{rate}{(s.Day is null ? "" : ", x." + day)} }}).FirstOrDefaultAsync();");
        o.Add($"    if ({n.Stored} is not null && {n.Stored}.{cat} == {n.Category}{sameDay})");
        o.Add("    {");
        o.Add($"        {n.Row}.{rate} = {n.Stored}.{rate};");
        o.Add($"        {n.Keep} = true;");
        o.Add("    }");
        o.Add("}");
        o.Add($"if (!{n.Keep})");
        o.Add("{");
        string dates = dating ? ", x." + s.ValidFrom!.CSharpName() + (s.ValidTo is null ? "" : ", x." + s.ValidTo.CSharpName()) : "";
        o.Add($"    var {n.Found} = await {n.Context}.Set<{s.CategoryTable.TableName}>().AsNoTracking().Where(x => x.{s.CategoryKey.CSharpName()} == {n.Category}).Select(x => new {{ x.{s.CategoryRate.CSharpName()}{dates} }}).FirstOrDefaultAsync();");
        o.AddRange(refuse($"if ({n.Found} is null)", false, TaxWords.Unknown("{" + n.Category + "}")).Select(l => "    " + l));
        if (dating)
        {
            string ended = s.ValidTo is null ? "" : $" || ({n.Found}.{s.ValidTo.CSharpName()} is {{ }} {n.ValidTo} && {n.ValidTo} < {n.AsOf})";
            o.AddRange(refuse($"if ({n.Found}.{s.ValidFrom!.CSharpName()} > {n.AsOf}{ended})", true, TaxWords.NotInForce("{" + n.Category + "}", "{" + n.AsOf + ":yyyy-MM-dd}")).Select(l => "    " + l));
        }
        o.Add($"    {n.Row}.{rate} = ({s.Rate.CSharpBase()}){n.Found}.{s.CategoryRate.CSharpName()};");
        o.Add("}");
        o.Add($"{n.Row}.{tax} = ({s.Tax.CSharpBase()})Math.Round((decimal){n.Row}.{s.Taxable.CSharpName()} * (decimal){n.Row}.{rate}, {s.Tax.Scale ?? TaxWords.DefaultScale}, MidpointRounding.AwayFromZero);");
        return o;
    }
}
