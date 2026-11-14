using System.Text.RegularExpressions;

namespace CodeGenNew.Core;

/// <summary> One line of a form page: the fields that sit side by side on it, and the check box that must be ticked for the line to be shown (null: always shown). </summary>
public sealed record FormLine(IReadOnlyList<ColumnModel> Columns, ColumnModel? ShownWhen);

/// <summary> How the fields of one form page are put on lines, the same for every stack. Fields that belong together share a line: a first and a last (<c>FirstInvoiceDate</c>, <c>LastInvoiceDate</c>),
/// a minimum and a maximum (<c>MinimumInvoiceAmount</c>, <c>MaximumInvoiceAmount</c>), the day, month and year of one date, and a column with the one that qualifies it (<c>QuantityUm</c>, <c>QuantityUmRatio</c>).
/// A check box that a column's name builds on (<c>IsReviewed</c>, <c>ReviewedOnDate</c>, <c>ReviewedBy</c>) shows those columns only while it is ticked. Only the names decide: a pair is never made of two
/// columns whose names merely look alike but whose words differ (<c>MinimumMargin</c> and <c>MaximumDiscount</c> stay apart). </summary>
public static class FormLayout
{
    /// <summary> The most pixels of fields the desktop puts on one line (the dialog's content is at least 560 wide, less its scroll bar and padding). </summary>
    public const int LineWidth = 500;

    /// <summary> The most fields a line holds when no widths are known. </summary>
    public const int MostFields = 3;

    private static readonly (string Low, string High)[] Bounds =
    [
        ("First", "Last"), ("Minimum", "Maximum"), ("Min", "Max"), ("Start", "End"), ("Begin", "End"), ("From", "To"), ("Low", "High"), ("Lower", "Upper"),
    ];

    private static readonly string[] Companions = ["Ratio", "Factor", "Multiplier", "Divisor", "Rate", "Scale"];

    private static readonly Regex DatePart = new(@"^(?<pre>.*?)(?<part>Year|Month|Day)(?:Number|Num|No|Nbr|Nr)?$", RegexOptions.Compiled);

    /// <param name="width"> The pixels a field needs; null when the stack has no widths (a line then holds <see cref="MostFields"/> at most). </param>
    public static List<FormLine> For(FormModel form, IReadOnlyList<ColumnModel> columns, Func<ColumnModel, int>? width = null)
    {
        var dependents = Dependents(form, columns);
        var claimed = dependents.SelectMany(d => d.Value).ToHashSet();
        var lines = new List<FormLine>();
        foreach (var group in Groups(form, columns.Where(c => !claimed.Contains(c)).ToList(), width))
        {
            lines.Add(new FormLine(group, null));
            foreach (var flag in group.Where(dependents.ContainsKey))
                lines.AddRange(Groups(form, dependents[flag], width).Select(g => new FormLine(g, flag)));
        }
        return lines;
    }

    /// <summary> The check boxes whose ticked state shows some other columns, over every page of the form, in order. </summary>
    public static List<ColumnModel> Flags(FormModel form) =>
        form.Pages.SelectMany(p => Dependents(form, p.Columns).Keys).ToList();

    /// <summary> The columns each check box shows while it is ticked: the nullable columns whose name starts with the check box's name less its Is or Has (<c>IsReviewed</c> shows <c>ReviewedOnDate</c> and
    /// <c>ReviewedBy</c>). A required column is never hidden, so a form can always be saved. </summary>
    private static Dictionary<ColumnModel, List<ColumnModel>> Dependents(FormModel form, IReadOnlyList<ColumnModel> columns)
    {
        var result = new Dictionary<ColumnModel, List<ColumnModel>>();
        var claimed = new HashSet<ColumnModel>();
        foreach (var flag in columns.Where(c => form.Field(c).IsBooleanField && Plain(form, c)))
        {
            string stem = Regex.Replace(flag.Name, "^(Is|Has)(?=[A-Z])", "");
            if (stem.Length < 4)
                continue;
            var shown = columns.Where(d => d != flag && !claimed.Contains(d) && d.IsNullable && Plain(form, d) && !form.Field(d).IsBooleanField
                                           && d.Name.Length > stem.Length && d.Name.StartsWith(stem, StringComparison.Ordinal) && char.IsUpper(d.Name[stem.Length])).ToList();
            if (shown.Count == 0)
                continue;
            claimed.UnionWith(shown);
            result[flag] = shown;
        }
        return result;
    }

    private static List<List<ColumnModel>> Groups(FormModel form, IReadOnlyList<ColumnModel> columns, Func<ColumnModel, int>? width)
    {
        var used = new HashSet<ColumnModel>();
        var groups = new List<List<ColumnModel>>();
        foreach (var column in columns)
        {
            if (used.Contains(column))
                continue;
            var members = Related(form, column, columns).Where(m => used.Add(m)).ToList();
            groups.AddRange(Chunk(members, width));
        }
        return groups;
    }

    /// <summary> Splits a group so that no line is wider than <see cref="LineWidth"/> (or longer than <see cref="MostFields"/> without widths). </summary>
    private static IEnumerable<List<ColumnModel>> Chunk(List<ColumnModel> members, Func<ColumnModel, int>? width)
    {
        var line = new List<ColumnModel>();
        int used = 0;
        foreach (var m in members)
        {
            int w = width?.Invoke(m) ?? 0;
            if (line.Count > 0 && (width is null ? line.Count >= MostFields : used + w > LineWidth))
            {
                yield return line;
                line = [];
                used = 0;
            }
            line.Add(m);
            used += w;
        }
        yield return line;
    }

    /// <summary> The columns that share a line with <paramref name="c"/> (itself included, in the order they should appear). </summary>
    private static List<ColumnModel> Related(FormModel form, ColumnModel c, IReadOnlyList<ColumnModel> columns)
    {
        if (!Plain(form, c))
            return [c];
        ColumnModel? Find(string name) => columns.FirstOrDefault(o => o != c && o.Name.EqualsIgnoreCase(name) && Plain(form, o));

        foreach (var (low, high) in Bounds)
        {
            foreach (var (mine, other, mineIsLow) in new[] { (low, high, true), (high, low, false) })
            {
                string? rest = c.Name.Length > mine.Length && c.Name.StartsWith(mine, StringComparison.Ordinal) && char.IsUpper(c.Name[mine.Length]) ? c.Name[mine.Length..] : null;
                var partner = rest is null ? null : Find(other + rest);
                if (partner is null && c.Name.Length > mine.Length && c.Name.EndsWith(mine, StringComparison.Ordinal))
                    partner = Find(c.Name[..^mine.Length] + other);
                if (partner is not null && SameKind(form, c, partner))
                    return mineIsLow ? [c, partner] : [partner, c];
            }
        }

        if (form.Field(c).IsWholeNumberValue && !form.Field(c).IsLookupField && DatePart.Match(c.Name) is { Success: true } mine2)
        {
            var parts = columns.Where(o => Plain(form, o) && form.Field(o).IsWholeNumberValue && !form.Field(o).IsLookupField
                                           && DatePart.Match(o.Name) is { Success: true } m && m.Groups["pre"].Value == mine2.Groups["pre"].Value).ToList();
            if (parts.Count > 1)
                return parts;
        }

        foreach (string word in Companions)
        {
            if (Find(c.Name + word) is { } companion && !form.Field(c).IsDateField && !form.Field(companion).IsDateField)
                return [c, companion];
            if (c.Name.Length > word.Length && c.Name.EndsWith(word, StringComparison.Ordinal) && Find(c.Name[..^word.Length]) is { } qualified && !form.Field(qualified).IsDateField)
                return [qualified, c];
        }
        return [c];
    }

    /// <summary> A field that may share a line or be hidden by a check box: an ordinary one-line field the system does not write, no guard, no feature flag, not the key. </summary>
    private static bool Plain(FormModel form, ColumnModel c)
    {
        var field = form.Field(c);
        return !c.IsPrimaryKey && !field.IsSystemStampField && !field.IsMultilineField && form.GuardOf(c) is null && form.FeatureOf(c) is null;
    }

    private static bool SameKind(FormModel form, ColumnModel a, ColumnModel b)
    {
        var x = form.Field(a);
        var y = form.Field(b);
        bool Number(FormColumn f) => (f.IsMoneyField || f.IsDecimalValue || f.IsWholeNumberValue) && !f.IsLookupField;
        return (x.IsDateField && y.IsDateField) || (Number(x) && Number(y));
    }
}
