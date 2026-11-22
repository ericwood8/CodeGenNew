using System.Globalization;
using System.Text;
using CodeGenNew.Core.Naming;

namespace CodeGenNew.Core.Totals;

/// <summary> The C# of <c>DocumentTotals</c>. <see cref="Context"/> is what the generated DbContext file holds (it is also the context of the desktop apps, so a desktop save works the totals out as well): per header a
/// static <c>&lt;Header&gt;Totals</c> class that notes the headers a save touches (the header, its lines and its allowance rows), and after the save, inside the same transaction, writes the worked-out totals.
/// <see cref="Check"/> is the ASP.NET part: the route that lists the headers whose stored totals differ from their rows. </summary>
public static class DocumentTotalsWriter
{
    private static string Name(ColumnModel column) => column.CSharpName().TrimStart('@');

    private static string Number(ColumnModel column, string expression) => column.IsNullable ? $"(decimal)({expression} ?? 0)" : $"(decimal){expression}";

    private static int Scale(ColumnModel? column) => column?.Scale ?? DocumentTotalsWords.DefaultScale;

    private static string Decimal(decimal value) => value.ToString(CultureInfo.InvariantCulture) + "m";

    public static string Context(IEnumerable<DocumentTotalsShape> shapes, string contextType)
    {
        var list = shapes.ToList();
        if (list.Count == 0)
            return "";
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        L();
        L("/// <summary> DocumentTotals: the totals of a document header are worked out again from its rows after every save that touches the header, its lines or its allowance rows, in the same transaction. </summary>");
        L("public static class DocumentTotals");
        L("{");
        L("    public interface IChanged");
        L("    {");
        L($"        Task LockAsync({contextType} context, CancellationToken cancellationToken);");
        L($"        void Lock({contextType} context);");
        L($"        Task ApplyAsync({contextType} context, CancellationToken cancellationToken);");
        L($"        void Apply({contextType} context);");
        L("    }");
        L();
        L("    /// <summary> The headers a save is about to touch. </summary>");
        L("    public sealed class Pending");
        L("    {");
        L("        public readonly List<IChanged> Items = [];");
        L("        public bool Any => Items.Count > 0;");
        L("    }");
        L();
        foreach (bool async in new[] { true, false })
        {
            L($"    public static {(async ? "async Task<Pending> CollectAsync" : "Pending Collect")}({contextType} context{(async ? ", CancellationToken cancellationToken" : "")})");
            L("    {");
            L("        var pending = new Pending();");
            foreach (var s in list)
                L($"        {(async ? "await " : "")}{s.Header.TableName}Totals.Collect{(async ? "Async" : "")}(context, pending{(async ? ", cancellationToken" : "")});");
            L("        return pending;");
            L("    }");
            L();
        }
        L("    /// <summary> Takes the header rows of the documents a save is about to touch before its first write, in key order: a line's own foreign key check holds a shared lock on its header, and two saves that each held one and then asked for the update lock would deadlock. </summary>");
        L($"    public static async Task LockAsync({contextType} context, Pending pending, CancellationToken cancellationToken)");
        L("    {");
        L("        foreach (var item in pending.Items)");
        L("            await item.LockAsync(context, cancellationToken);");
        L("    }");
        L();
        L($"    public static void Lock({contextType} context, Pending pending)");
        L("    {");
        L("        foreach (var item in pending.Items)");
        L("            item.Lock(context);");
        L("    }");
        L();
        L($"    public static async Task ApplyAsync({contextType} context, Pending pending, CancellationToken cancellationToken)");
        L("    {");
        L("        foreach (var item in pending.Items)");
        L("            await item.ApplyAsync(context, cancellationToken);");
        L("    }");
        L();
        L($"    public static void Apply({contextType} context, Pending pending)");
        L("    {");
        L("        foreach (var item in pending.Items)");
        L("            item.Apply(context);");
        L("    }");
        L("}");
        foreach (var s in list)
            o.Append(Header(s, contextType));
        return o.ToString();
    }

    private static string Header(DocumentTotalsShape s, string contextType)
    {
        string header = s.Header.TableName, key = Name(s.Key), keyType = s.Key.CSharpBase(), lines = s.Lines.TableName;
        var c = s.Columns;
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        L();
        L($"/// <summary> DocumentTotals of {header}: what a save of the header, of {lines}" + (s.Allowance is null ? "" : $" or of {s.Allowance.Table.TableName}") + " writes into the totals. </summary>");
        L($"public static class {header}Totals");
        L("{");
        L("    public sealed record Worked(decimal Lines, decimal Allowances, decimal Charges, decimal TaxExclusive, decimal Tax, decimal TaxInclusive, decimal Prepaid, decimal Rounding, decimal Payable);");
        L();
        L("    private static decimal Round(decimal value, int scale) => Math.Round(value, scale, MidpointRounding.AwayFromZero);");
        L();
        L("    /// <summary> The decimal places of an amount in <paramref name=\"currency\"/>: the places of the currency, at most the scale of the column that holds the amount. </summary>");
        L("    private static int Places(string? currency, int scale)");
        L("    {");
        var (specificPlaces, anyPlaces) = DocumentTotalsCurrencyRules.Split(s.Places);
        foreach (var r in specificPlaces)
            L($"        if (string.Equals(currency?.Trim(), \"{r.Currency}\", StringComparison.OrdinalIgnoreCase))\n            return Math.Min({r.Places}, scale);");
        L(anyPlaces is null ? "        return scale;" : $"        return Math.Min({anyPlaces.Places}, scale);");
        L("    }");
        L();
        L("    /// <summary> The multiple the payable amount is rounded to in <paramref name=\"currency\"/>, or 0 for none. </summary>");
        L("    private static decimal Increment(string? currency)");
        L("    {");
        var (specificRounds, any) = DocumentTotalsCurrencyRules.Split(s.Rounds);
        foreach (var r in specificRounds)
            L($"        if (string.Equals(currency?.Trim(), \"{r.Currency}\", StringComparison.OrdinalIgnoreCase))\n            return {Decimal(r.Increment)};");
        L($"        return {(any is null ? "0m" : Decimal(any.Increment))};");
        L("    }");
        L();
        L("    /// <summary> The totals from the sums of the rows: every amount is rounded to the scale of its column, half away from zero, and the next one is worked out from the rounded ones. </summary>");
        L("    public static Worked Work(decimal lines, decimal allowances, decimal charges, decimal tax, decimal prepaid, string? currency)");
        L("    {");
        L($"        lines = Round(lines, Places(currency, {Scale(c.Lines)}));");
        L($"        allowances = Round(allowances, Places(currency, {Scale(c.Allowances)}));");
        L($"        charges = Round(charges, Places(currency, {Scale(c.Charges)}));");
        L($"        decimal exclusive = Round(lines - allowances + charges, Places(currency, {Scale(c.TaxExclusive)}));");
        L($"        tax = Round(tax, Places(currency, {Scale(c.Tax)}));");
        L($"        decimal inclusive = Round(exclusive + tax, Places(currency, {Scale(c.TaxInclusive)}));");
        L("        decimal due = inclusive - prepaid;");
        L("        decimal step = Increment(currency);");
        L($"        decimal rounding = step > 0 ? Round(Math.Round(due / step, 0, MidpointRounding.AwayFromZero) * step - due, Places(currency, {Scale(c.Rounding)})) : 0m;");
        L($"        return new Worked(lines, allowances, charges, exclusive, tax, inclusive, prepaid, rounding, Round(due + rounding, Places(currency, {Scale(c.Payable)})));");
        L("    }");
        L();
        L("    private static void Note(HashSet<" + keyType + "> keys, object? value)");
        L("    {");
        L("        if (value is not null)");
        L($"            keys.Add(({keyType})Convert.ChangeType(value, typeof({keyType})));");
        L("    }");
        L();
        L($"    private sealed class Changed : DocumentTotals.IChanged");
        L("    {");
        L($"        public readonly HashSet<{keyType}> Keys = [];");
        L($"        public readonly List<{header}> Headers = [];");
        L();
        foreach (bool async in new[] { true, false })
        {
            L($"        public {(async ? "async Task LockAsync" : "void Lock")}({contextType} context{(async ? ", CancellationToken cancellationToken" : "")})");
            L("        {");
            L($"            foreach (var id in Keys.Order())\n                {(async ? "await " : "")}LockTotals{(async ? "Async" : "")}(context, id{(async ? ", cancellationToken" : "")});");
            L("        }");
            L();
        }
        foreach (bool async in new[] { true, false })
        {
            string a = async ? "async Task ApplyAsync" : "void Apply", t = async ? ", CancellationToken cancellationToken" : "", w = async ? "await " : "", suffix = async ? "Async" : "", token = async ? ", cancellationToken" : "";
            L($"        public {a}({contextType} context{t})");
            L("        {");
            L($"            foreach (var header in Headers)\n                Keys.Add(header.{key});");
            L($"            foreach (var id in Keys.Order())\n                {w}RecalculateTotals{suffix}(context, id{token});");
            L($"            foreach (var entry in context.ChangeTracker.Entries<{header}>().Where(e => Keys.Contains(e.Entity.{key})).ToList())\n                {w}entry.Reload{suffix}({(async ? "cancellationToken" : "")});");
            L("        }");
            L();
        }
        L("    }");
        L();
        foreach (bool async in new[] { true, false })
            Collect(o, s, contextType, async);
        foreach (bool async in new[] { true, false })
            Recalculate(o, s, contextType, async);
        L("}");
        return o.ToString();
    }

    /// <summary> <c>Collect</c>: notes the headers the pending save touches. The header a changed or deleted line sat on before is read from the database, since an update that came in as a whole row carries no old value. </summary>
    private static void Collect(StringBuilder o, DocumentTotalsShape s, string contextType, bool async)
    {
        string header = s.Header.TableName, lines = s.Lines.TableName;
        string suffix = async ? "Async" : "", w = async ? "await " : "", token = async ? ", CancellationToken cancellationToken" : "", arg = async ? ", cancellationToken" : "";
        void L(string text = "") => o.Append(text).Append('\n');
        void Rows(string variable, TableModel table, ColumnModel foreign)
        {
            ColumnModel pk = table.PrimaryKeyColumns[0];
            L($"                Note(changed.Keys, {variable}.{Name(foreign)});");
            L("                if (entry.State != EntityState.Added)");
            L($"                    Note(changed.Keys, {w}context.Set<{table.TableName}>().AsNoTracking().Where(x => x.{Name(pk)} == {variable}.{Name(pk)}).Select(x => x.{Name(foreign)}).FirstOrDefault{suffix}({(async ? "cancellationToken" : "")}));");
        }
        L();
        L($"    public static {(async ? "async Task" : "void")} Collect{suffix}({contextType} context, DocumentTotals.Pending pending{token})");
        L("    {");
        L("        var changed = new Changed();");
        L("        foreach (var entry in context.ChangeTracker.Entries().ToList())");
        L("        {");
        L("            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))");
        L("                continue;");
        L($"            if (entry.Entity is {lines} line)");
        L("            {");
        Rows("line", s.Lines, s.LineKey);
        L("            }");
        if (s.Allowance is { Document: { } document } allowance)
        {
            L($"            else if (entry.Entity is {allowance.Table.TableName} extra)");
            L("            {");
            Rows("extra", allowance.Table, document);
            L("            }");
        }
        L($"            else if (entry.Entity is {header} header && entry.State != EntityState.Deleted)");
        L("                changed.Headers.Add(header);");
        L("        }");
        L("        if (changed.Keys.Count > 0 || changed.Headers.Count > 0)");
        L("            pending.Items.Add(changed);");
        L("    }");
    }

    /// <summary> <c>WorkOut</c> and <c>RecalculateTotals</c>: read the rows of the document, work the totals out and write them into the header; the header row is written first, which makes a second save of the same document wait for the first. </summary>
    private static void Recalculate(StringBuilder o, DocumentTotalsShape s, string contextType, bool async)
    {
        string header = s.Header.TableName, key = Name(s.Key), keyType = s.Key.CSharpBase();
        var c = s.Columns;
        string suffix = async ? "Async" : "", w = async ? "await " : "", token = async ? ", cancellationToken" : "", parameter = async ? ", CancellationToken cancellationToken = default" : "";
        string list = async ? "ToListAsync(cancellationToken)" : "ToList()";
        void L(string text = "") => o.Append(text).Append('\n');
        L();
        L($"    /// <summary> The totals of one document worked out from its rows. </summary>");
        L($"    public static {(async ? "async Task<Worked?>" : "Worked?")} WorkOut{suffix}({contextType} context, {keyType} id{parameter})");
        L("    {");
        bool headerRead = c.Prepaid is not null || s.Currency is not null;
        if (headerRead)
        {
            L($"        var header = {w}context.Set<{header}>().AsNoTracking().Where(x => x.{key} == id).Select(x => new {{ Prepaid = {(c.Prepaid is null ? "0" : "x." + Name(c.Prepaid))}, Currency = {(s.Currency is null ? "(string?)null" : "x." + Name(s.Currency))} }}).FirstOrDefault{suffix}({(async ? "cancellationToken" : "")});");
            L("        if (header is null)");
            L("            return null;");
        }
        else
        {
            L($"        if (!{w}context.Set<{header}>().AsNoTracking().Any{suffix}(x => x.{key} == id{token}))");
            L("            return null;");
        }
        L($"        decimal lines = ({w}context.Set<{s.Lines.TableName}>().AsNoTracking().Where(x => x.{Name(s.LineKey)} == id).Select(x => x.{Name(s.LineAmount)}).{list}).Sum(v => {Number(s.LineAmount, "v")});");
        if (s.Allowance is { Document: { } doc } allowance)
        {
            L($"        var extras = {w}context.Set<{allowance.Table.TableName}>().AsNoTracking().Where(x => x.{Name(doc)} == id).Select(x => new {{ Charge = x.{Name(allowance.Flag)}, Amount = x.{Name(allowance.Amount)}{(s.AllowanceTax is null ? "" : $", Tax = x.{Name(s.AllowanceTax)}")} }}).{list};");
            L($"        decimal allowances = extras.Where(r => !r.Charge).Sum(r => {Number(allowance.Amount, "r.Amount")});");
            L($"        decimal charges = extras.Where(r => r.Charge).Sum(r => {Number(allowance.Amount, "r.Amount")});");
        }
        else
        {
            L("        decimal allowances = 0m, charges = 0m;");
        }
        if (s.Tax is { } tax)
            L($"        decimal taxes = ({w}context.Set<{s.Lines.TableName}>().AsNoTracking().Where(x => x.{Name(s.LineKey)} == id).Select(x => x.{Name(tax.Tax)}).{list}).Sum(v => {Number(tax.Tax, "v")});");
        else
            L("        decimal taxes = 0m;");
        if (s.AllowanceTax is not null)
            L($"        taxes += extras.Sum(r => (r.Charge ? 1 : -1) * {Number(s.AllowanceTax, "r.Tax")});");
        L($"        return Work(lines, allowances, charges, taxes, {(c.Prepaid is null ? "0m" : Number(c.Prepaid, "header.Prepaid"))}, {(s.Currency is null ? "null" : "header.Currency")});");
        L("    }");
        L();
        string lockProperty = Name(c.Owned().First().Column);
        L($"    private static {(async ? "async Task" : "void")} LockTotals{suffix}({contextType} context, {keyType} id{parameter})");
        L("    {");
        L($"        {w}context.Set<{header}>().Where(x => x.{key} == id).ExecuteUpdate{suffix}(s => s.SetProperty(x => x.{lockProperty}, x => x.{lockProperty}){token});");
        L("    }");
        L();
        L($"    /// <summary> Writes the totals of one document into its header. The header row is updated first, so a second save of the same document waits for the first one to commit before it reads the rows. </summary>");
        L($"    private static {(async ? "async Task" : "void")} RecalculateTotals{suffix}({contextType} context, {keyType} id{parameter})");
        L("    {");
        var owned = c.Owned().ToList();
        string first = Name(owned[0].Column), firstType = owned[0].Column.CSharpBase() + (owned[0].Column.IsNullable ? "?" : "");
        L($"        if ({w}context.Set<{header}>().Where(x => x.{key} == id).ExecuteUpdate{suffix}(s => s.SetProperty(x => x.{first}, x => x.{first}){token}) == 0)");
        L("            return;");
        L($"        var worked = {w}WorkOut{suffix}(context, id{token});");
        L("        if (worked is null)");
        L("            return;");
        var sets = new StringBuilder();
        foreach (var (part, column) in owned)
        {
            string value = part switch
            {
                DocumentTotalsWords.PartLines => "worked.Lines",
                DocumentTotalsWords.PartAllowances => "worked.Allowances",
                DocumentTotalsWords.PartCharges => "worked.Charges",
                DocumentTotalsWords.PartTaxExclusive => "worked.TaxExclusive",
                DocumentTotalsWords.PartTax => "worked.Tax",
                DocumentTotalsWords.PartTaxInclusive => "worked.TaxInclusive",
                DocumentTotalsWords.PartRounding => "worked.Rounding",
                _ => "worked.Payable",
            };
            sets.Append($"\n                .SetProperty(x => x.{Name(column)}, ({column.CSharpBase()}{(column.IsNullable ? "?" : "")}){value})");
        }
        L($"        {w}context.Set<{header}>().Where(x => x.{key} == id).ExecuteUpdate{suffix}(s => s{sets}{(async ? ",\n            cancellationToken" : "")});");
        L("    }");
    }

    /// <summary> The ASP.NET route of the header table: <c>GET document-totals-check?id=</c> answers the headers (one, or up to a thousand) whose stored totals differ from the worked-out ones. </summary>
    public static string Check(DocumentTotalsShape s, string contextType, ColumnModel? owner = null)
    {
        string header = s.Header.TableName, key = Name(s.Key);
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        L("    /// <summary> The headers whose stored totals differ from their rows (one header when <paramref name=\"id\"/> is given). </summary>");
        L($"    public static async Task<IResult> DocumentTotalsCheckAsync({contextType} context, " + (owner is null ? "" : "HttpContext http, ") + $"{s.Key.CSharpBase()}? id)");
        L("    {");
        L($"        var query = context.Set<{header}>().AsNoTracking();");
        if (owner is not null)
        {
            L("        if (await http.RequestServices.GetRequiredService<IOwnerScope>().VisibleOwnersAsync(http.User, http.RequestAborted) is { } owners)");
            L("        {");
            L("            var visible = owners.ToArray();");
            L($"            query = query.Where(x => visible.Contains(x.{Name(owner)}));");
            L("        }");
        }
        L("        if (id is not null)");
        L($"            query = query.Where(x => x.{key} == id);");
        L($"        var rows = await query.OrderBy(x => x.{key}).Take({DocumentTotalsWords.CheckLimit}).ToListAsync();");
        L("        var differing = new List<object>();");
        L("        foreach (var row in rows)");
        L("        {");
        L($"            var worked = await {header}Totals.WorkOutAsync(context, row.{key});");
        L("            if (worked is null)");
        L("                continue;");
        L("            var parts = new List<object>();");
        L("            void Compare(string part, decimal stored, decimal expected, int scale)");
        L("            {");
        L("                if (Math.Abs(stored - expected) >= 0.5m / (decimal)Math.Pow(10, scale))");
        L("                    parts.Add(new { Part = part, Stored = stored, Expected = expected });");
        L("            }");
        foreach (var (part, column) in s.Columns.Owned())
        {
            string expected = part switch
            {
                DocumentTotalsWords.PartLines => "worked.Lines",
                DocumentTotalsWords.PartAllowances => "worked.Allowances",
                DocumentTotalsWords.PartCharges => "worked.Charges",
                DocumentTotalsWords.PartTaxExclusive => "worked.TaxExclusive",
                DocumentTotalsWords.PartTax => "worked.Tax",
                DocumentTotalsWords.PartTaxInclusive => "worked.TaxInclusive",
                DocumentTotalsWords.PartRounding => "worked.Rounding",
                _ => "worked.Payable",
            };
            L($"            Compare(\"{part}\", {Number(column, "row." + Name(column))}, {expected}, {Scale(column)});");
        }
        L("            if (parts.Count > 0)");
        L($"                differing.Add(new {{ Id = row.{key}, Parts = parts }});");
        L("        }");
        L("        return Results.Ok(new { Checked = rows.Count, Differing = differing.Count, Headers = differing });");
        L("    }");
        L();
        return o.ToString();
    }
}
