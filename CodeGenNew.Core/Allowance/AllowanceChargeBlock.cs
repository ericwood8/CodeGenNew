namespace CodeGenNew.Core.Allowance;

/// <summary> The variable names of one place that writes <see cref="AllowanceChargeBlock"/>: the entity variable and the three decimals the work is done in. </summary>
public sealed record AllowanceChargeNames(string Row, string Base, string Percent, string Amount);

/// <summary> The C# that works out an allowance or charge row, shared by the API's <c>CheckAllowanceCharge</c> and the WinUI3 dialog's check: a negative value, a percentage with no base and a row with nothing to work an
/// amount from are refused; otherwise the amount is the base times the percent over 100, or the percentage is the amount over the base times 100. </summary>
public static class AllowanceChargeBlock
{
    /// <summary> What a caller writes to refuse a row: the line that opens the test (<c>if (...)</c> or <c>else if (...)</c>) and the message. </summary>
    public delegate IEnumerable<string> Refusal(string condition, string message);

    private static string Value(ColumnModel column, string row) => column.IsNullable ? $"(decimal)({row}.{column.CSharpName()} ?? 0)" : $"(decimal){row}.{column.CSharpName()}";

    /// <summary> The lines from the three decimals to the last test, indented from zero. </summary>
    public static IEnumerable<string> Lines(AllowanceChargeShape s, AllowanceChargeNames n, Refusal refuse)
    {
        string amount = s.Amount.CSharpName(), percent = s.Percent.CSharpName();
        var o = new List<string>
        {
            $"decimal {n.Base} = {Value(s.Base, n.Row)}, {n.Percent} = {Value(s.Percent, n.Row)}, {n.Amount} = (decimal){n.Row}.{amount};"
        };
        o.AddRange(refuse($"if ({n.Base} < 0 || {n.Percent} < 0 || {n.Amount} < 0)", AllowanceChargeWords.Negative));
        o.AddRange(refuse($"if ({n.Percent} > 0 && {n.Base} <= 0)", AllowanceChargeWords.PercentNeedsBase));
        o.Add($"if ({n.Percent} > 0)");
        o.Add($"    {n.Row}.{amount} = ({s.Amount.CSharpBase()})Math.Round({n.Base} * {n.Percent} / 100m, {s.Amount.Scale ?? AllowanceChargeWords.DefaultScale}, MidpointRounding.AwayFromZero);");
        o.Add($"else if ({n.Base} > 0 && {n.Amount} > 0)");
        o.Add($"    {n.Row}.{percent} = ({s.Percent.CSharpBase()})Math.Round({n.Amount} / {n.Base} * 100m, {s.Percent.Scale ?? AllowanceChargeWords.DefaultScale}, MidpointRounding.AwayFromZero);");
        o.AddRange(refuse($"else if ({n.Amount} <= 0)", AllowanceChargeWords.NeedsAmount));
        return o;
    }
}
