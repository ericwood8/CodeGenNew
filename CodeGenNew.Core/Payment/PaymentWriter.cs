namespace CodeGenNew.Core.Payment;

/// <summary> The C# of the payment rules (<see cref="PaymentPlan"/>), written into the table's <c>&lt;Table&gt;Shape</c> class by <see cref="RowShapeWriter"/>. Amounts are compared and added in memory, as decimals,
/// so a SQLite database (which holds a decimal as text) answers as the others do. </summary>
public static class PaymentWriter
{
    /// <summary> A date column is a <c>DateTime</c> in the generated entities (<see cref="ColumnModel.CSharpBase"/>), so the day is its <c>Date</c>. </summary>
    private static string Day(string expression) => $"{expression}.Date";

    private static string Store(ColumnModel column, string expression) => column.IsNullable ? expression : expression + " ?? \"\"";

    public const string AccountSaveLines = "        if (await CheckAccountAsync(context, row) is { } accountProblem)\n            return accountProblem;\n";

    public const string InstallmentSaveLines = "        if (await CheckInstallmentAsync(context, row) is { } installmentProblem)\n            return installmentProblem;\n";

    public const string DueSaveLines = "        if (await CheckDueAsync(context, row) is { } dueProblem)\n            return dueProblem;\n";

    public static string AccountCode(BankAccountShape s, string contextType)
    {
        var o = new System.Text.StringBuilder();
        o.Append("    private static readonly Dictionary<string, int> IbanLengths = \"" + PaymentPlan.IbanLengths + "\".Split(' ').ToDictionary(x => x[..2], x => int.Parse(x[2..]));\n\n");
        o.Append("""
    /// <summary> An IBAN has the length of its country, and the number of its text with the first four characters moved to the end and the letters written as 10 to 35 leaves 1 when divided by 97. </summary>
    public static bool IbanValid(string iban)
    {
        if (iban.Length < 5 || !IbanLengths.TryGetValue(iban[..2], out int length) || iban.Length != length || !iban.All(char.IsAsciiLetterOrDigit) || !char.IsAsciiLetter(iban[0]) || !char.IsAsciiLetter(iban[1]) || !char.IsAsciiDigit(iban[2]) || !char.IsAsciiDigit(iban[3]))
            return false;
        int remainder = 0;
        foreach (char c in iban[4..] + iban[..4])
            foreach (char digit in char.IsAsciiDigit(c) ? c.ToString() : (c - 'A' + 10).ToString())
                remainder = (remainder * 10 + (digit - '0')) % 97;
        return remainder == 1;
    }

    /// <summary> A routing number has nine digits and 3 * (1st + 4th + 7th) + 7 * (2nd + 5th + 8th) + (3rd + 6th + 9th) is a multiple of ten. </summary>
    public static bool RoutingValid(string routing)
    {
        if (routing.Length != 9 || !routing.All(char.IsAsciiDigit))
            return false;
        int[] d = routing.Select(c => c - '0').ToArray();
        return (3 * (d[0] + d[3] + d[6]) + 7 * (d[1] + d[4] + d[7]) + (d[2] + d[5] + d[8])) % 10 == 0;
    }


""");
        o.Append("    /// <summary> The IBAN is written without spaces and in upper case and must be valid; an account number with a routing number must be valid too; one of the two is needed. </summary>\n");
        o.Append($"    public static Task<IResult?> CheckAccountAsync({contextType} context, {s.Table.TableName} row)\n    {{\n");
        if (s.Iban is { } iban)
        {
            string name = iban.CSharpName();
            o.Append($"        string? iban = row.{name} is null ? null : new string(row.{name}.Where(c => c != ' ' && c != '-').ToArray()).ToUpperInvariant();\n");
            o.Append("        if (string.IsNullOrEmpty(iban))\n            iban = null;\n");
            o.Append($"        row.{name} = {Store(iban, "iban")};\n");
            o.Append($"        if (iban is not null && !IbanValid(iban))\n            return Task.FromResult<IResult?>(RowShapeProblems.PaymentInvalid(\"{PaymentWords.IbanBad}\"));\n");
        }
        else
            o.Append("        string? iban = null;\n");
        if (s.Account is { } account && s.Routing is { } routing)
        {
            string a = account.CSharpName(), r = routing.CSharpName();
            o.Append($"        string? account = string.IsNullOrWhiteSpace(row.{a}) ? null : row.{a}.Trim();\n        string? routing = string.IsNullOrWhiteSpace(row.{r}) ? null : row.{r}.Trim();\n");
            o.Append($"        row.{a} = {Store(account, "account")};\n        row.{r} = {Store(routing, "routing")};\n");
            o.Append($"        if (routing is not null && !RoutingValid(routing))\n            return Task.FromResult<IResult?>(RowShapeProblems.PaymentInvalid(\"{PaymentWords.RoutingBad}\"));\n");
            o.Append($"        if (account is not null && routing is not null && (account.Length is < 4 or > 17 || !account.All(char.IsAsciiDigit)))\n            return Task.FromResult<IResult?>(RowShapeProblems.PaymentInvalid(\"{PaymentWords.AccountNumberBad}\"));\n");
            o.Append($"        if (iban is null && (account is null || routing is null))\n            return Task.FromResult<IResult?>(RowShapeProblems.PaymentInvalid(\"{PaymentWords.AccountMissing}\"));\n");
        }
        else
            o.Append($"        if (iban is null)\n            return Task.FromResult<IResult?>(RowShapeProblems.PaymentInvalid(\"{PaymentWords.AccountMissing}\"));\n");
        o.Append("        return Task.FromResult<IResult?>(null);\n    }\n\n");
        return o.ToString();
    }

    public static string InstallmentCode(InstallmentShape s, string contextType)
    {
        string table = s.Table.TableName, documents = s.DocumentTable.TableName, doc = s.Document.CSharpName(), seq = s.Sequence.CSharpName(), due = s.Due.CSharpName(), amount = s.Amount.CSharpName(), key = s.Key.CSharpName();
        string documentKey = s.DocumentKey.CSharpName(), total = s.Total.CSharpName(), documentType = s.Document.CSharpBase();
        return $$"""
    /// <summary> The document exists, the sequence number and the amount are above zero, no other installment of the document has the number, the due dates follow the numbers, and the installments add up to no more than the total of the document. </summary>
    public static async Task<IResult?> CheckInstallmentAsync({{contextType}} context, {{table}} row)
    {
        if (!await context.Set<{{documents}}>().AsNoTracking().AnyAsync(d => d.{{documentKey}} == row.{{doc}}))
            return RowShapeProblems.PaymentInvalid("{{PaymentWords.DocumentMissing}}");
        if ((decimal)row.{{seq}} <= 0)
            return RowShapeProblems.PaymentInvalid("{{PaymentWords.SequenceNotPositive}}");
        if ((decimal)row.{{amount}} <= 0)
            return RowShapeProblems.PaymentInvalid("{{PaymentWords.AmountNotPositive}}");
        var others = await context.Set<{{table}}>().AsNoTracking().Where(x => x.{{key}} != row.{{key}} && x.{{doc}} == row.{{doc}}).Select(x => new { x.{{seq}}, x.{{due}}, x.{{amount}} }).ToListAsync();
        if (others.Any(x => x.{{seq}} == row.{{seq}}))
            return RowShapeProblems.PaymentInvalid("{{PaymentWords.SequenceTaken}}");
        if (others.Any(x => x.{{seq}} < row.{{seq}} && x.{{due}} > row.{{due}} || x.{{seq}} > row.{{seq}} && x.{{due}} < row.{{due}}))
            return RowShapeProblems.PaymentInvalid("{{PaymentWords.OutOfOrder}}");
        var held = await context.Set<{{documents}}>().AsNoTracking().Where(d => d.{{documentKey}} == row.{{doc}}).Select(d => new { d.{{total}} }).FirstAsync();
        return others.Sum(x => (decimal)x.{{amount}}) + (decimal)row.{{amount}} > (decimal)held.{{total}} ? RowShapeProblems.InstallmentOverTotal("{{PaymentWords.OverTotal}}") : null;
    }

    /// <summary> What the installments of a document come to: the total, the sum scheduled, what is left to schedule and whether the schedule is complete. </summary>
    public static async Task<IResult> ScheduleAsync({{contextType}} context, {{documentType}}? document)
    {
        if (document is null)
            return Results.Problem(statusCode: 400, title: "{{PaymentWords.NeedsDocument}}");
        var held = await context.Set<{{documents}}>().AsNoTracking().Where(d => d.{{documentKey}} == document).Select(d => new { d.{{total}} }).FirstOrDefaultAsync();
        if (held is null)
            return RowShapeProblems.PaymentInvalid("{{PaymentWords.DocumentMissing}}");
        var rows = await context.Set<{{table}}>().AsNoTracking().Where(x => x.{{doc}} == document).Select(x => x.{{amount}}).ToListAsync();
        decimal total = (decimal)held.{{total}}, scheduled = rows.Sum(x => (decimal)x);
        return Results.Ok(new { document, total, scheduled, remaining = total - scheduled, complete = scheduled == total, installments = rows.Count });
    }


""";
    }

    public static string InstallmentMapLine(InstallmentShape s) =>
        $"        app.MapGet(\"§R/schedule\", ([FromServices] §CTX context, [FromQuery(Name = \"document\")] {s.Document.CSharpBase()}? document) => ScheduleAsync(context, document))§AUTH;\n";

    public static string DueMapLine(DueDateShape s) =>
        s.CanPay ? $"        app.MapGet(\"§R/payable\", ([FromServices] §CTX context, [FromQuery(Name = \"id\")] {s.Key.CSharpBase()}? id, [FromQuery(Name = \"on\")] DateTime? on) => PayableAsync(context, id, on))§AUTH;\n" : "";

    /// <summary> What is payable on a day: the total less the early-payment discount while the discount days last, plus the late penalty once the due day has passed. </summary>
    private static string PayableCode(DueDateShape s, string contextType)
    {
        string table = s.Table.TableName, documents = s.DocumentTable.TableName, terms = s.TermsTable.TableName, doc = s.Document.CSharpName(), termsColumn = s.Terms.CSharpName(), due = s.Due.CSharpName();
        string documentKey = s.DocumentKey.CSharpName(), date = s.DocumentDate.CSharpName(), total = s.Total!.CSharpName(), termsKey = s.TermsKey.CSharpName(), key = s.Key.CSharpName();
        var members = new List<string>();
        var assigns = new List<string>();
        if (s.EarlyPercent is not null && s.EarlyDays is not null)
        {
            members.Add("t." + s.EarlyPercent.CSharpName());
            members.Add("t." + s.EarlyDays.CSharpName());
            assigns.Add($"earlyPercent = (decimal?)held.{s.EarlyPercent.CSharpName()}; earlyDays = (int?)held.{s.EarlyDays.CSharpName()};");
        }
        if (s.LatePercent is not null)
        {
            members.Add("t." + s.LatePercent.CSharpName());
            assigns.Add($"latePercent = (decimal?)held.{s.LatePercent.CSharpName()};");
        }
        string start = Day("doc." + date + (s.DocumentDate.IsNullable ? ".Value" : ""));
        string earlyIf = "if (earlyPercent is not null && earlyDays is not null" + (s.DocumentDate.IsNullable ? " && doc." + date + " is not null" : "") + " && day <= " + start + ".AddDays(earlyDays.Value))";
        string early = s.EarlyPercent is null ? "" : earlyIf + "\n        {\n            discount = Math.Round(total * earlyPercent.Value / 100m, scale, MidpointRounding.AwayFromZero);\n            rule = \"" + PaymentWords.RuleEarly + "\";\n        }\n";
        string lateIf = "if (latePercent is not null && row." + due + " is { } dueDay && day > " + Day("dueDay") + ")";
        string late = s.LatePercent is null ? "" : lateIf + "\n        {\n            penalty = Math.Round(total * latePercent.Value / 100m, scale, MidpointRounding.AwayFromZero);\n            rule = \"" + PaymentWords.RuleLate + "\";\n        }\n";
        string tail = early.Length > 0 && late.Length > 0 ? early + "        else " + late : early + late;
        int scale = s.Total.Scale ?? 2;
        string assignText = string.Join("\n                ", assigns);
        return $$"""
    /// <summary> What is payable on a day (today without one): the total less the early-payment discount while the discount days of the terms last, plus the late penalty once the due day has passed. </summary>
    public static async Task<IResult> PayableAsync({{contextType}} context, {{s.Key.CSharpBase()}}? id, DateTime? on)
    {
        if (id is null)
            return Results.Problem(statusCode: 400, title: "{{PaymentWords.NeedsPayment}}");
        var row = await context.Set<{{table}}>().AsNoTracking().Where(x => x.{{key}} == id).Select(x => new { x.{{doc}}, x.{{termsColumn}}, x.{{due}} }).FirstOrDefaultAsync();
        if (row is null)
            return RowShapeProblems.PaymentInvalid("{{PaymentWords.PaymentMissing}}");
        var doc = await context.Set<{{documents}}>().AsNoTracking().Where(d => d.{{documentKey}} == row.{{doc}}).Select(d => new { d.{{date}}, d.{{total}} }).FirstOrDefaultAsync();
        if (doc is null)
            return RowShapeProblems.PaymentInvalid("{{PaymentWords.DocumentMissing}}");
        decimal total = (decimal)doc.{{total}}, discount = 0, penalty = 0;
        DateTime day = (on ?? DateTime.Today).Date;
        decimal? earlyPercent = null, latePercent = null;
        int? earlyDays = null;
        if (row.{{termsColumn}} is not null)
        {
            var held = await context.Set<{{terms}}>().AsNoTracking().Where(t => t.{{termsKey}} == row.{{termsColumn}}).Select(t => new { {{string.Join(", ", members)}} }).FirstOrDefaultAsync();
            if (held is not null)
            {
                {{assignText}}
            }
        }
        int scale = {{scale}};
        string rule = "{{PaymentWords.RuleOnTime}}";
        {{tail}}        return Results.Ok(new { id, document = row.{{doc}}, total, day, dueDate = row.{{due}}, discount, penalty, payable = total - discount + penalty, rule });
    }

""";
    }

    public static string DueCode(DueDateShape s, string contextType)
    {
        return DueCheckCode(s, contextType) + (s.CanPay ? PayableCode(s, contextType) : "");
    }

    private static string DueCheckCode(DueDateShape s, string contextType)
    {
        string table = s.Table.TableName, documents = s.DocumentTable.TableName, terms = s.TermsTable.TableName, doc = s.Document.CSharpName(), termsColumn = s.Terms.CSharpName(), due = s.Due.CSharpName();
        string documentKey = s.DocumentKey.CSharpName(), date = s.DocumentDate.CSharpName(), termsKey = s.TermsKey.CSharpName(), days = s.Days.CSharpName();
        string start = Day("held." + date + (s.DocumentDate.IsNullable ? ".Value" : ""));
        string noDate = s.DocumentDate.IsNullable ? "        if (held." + date + " is null)\n            return null;\n" : "";
        return $$"""
    /// <summary> A row with payment terms and no due date gets the date of the document plus the days to pay; a due date before the date of the document is refused. </summary>
    public static async Task<IResult?> CheckDueAsync({{contextType}} context, {{table}} row)
    {
        var held = await context.Set<{{documents}}>().AsNoTracking().Where(d => d.{{documentKey}} == row.{{doc}}).Select(d => new { d.{{date}} }).FirstOrDefaultAsync();
        if (held is null)
            return RowShapeProblems.PaymentInvalid("{{PaymentWords.DocumentMissing}}");
{{noDate}}        DateTime start = {{start}};
        if (row.{{due}} is null)
        {
            var termsKey = row.{{termsColumn}};
            if (termsKey is null)
                return null;
            var found = await context.Set<{{terms}}>().AsNoTracking().Where(t => t.{{termsKey}} == termsKey).Select(t => new { t.{{days}} }).FirstOrDefaultAsync();
            if (found is null)
                return RowShapeProblems.PaymentInvalid("{{PaymentWords.TermsMissing}}");
            row.{{due}} = {{"start.AddDays(found." + days + ")"}};
            return null;
        }
        return {{Day("row." + due + ".Value")}} < start ? RowShapeProblems.PaymentInvalid("{{PaymentWords.DueBeforeDocument}}") : null;
    }


""";
    }
}
