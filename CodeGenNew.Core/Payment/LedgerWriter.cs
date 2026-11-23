namespace CodeGenNew.Core.Payment;

/// <summary> The C# of the payment ledger (<see cref="LedgerPlan"/>), written into the table's <c>&lt;Table&gt;Shape</c> class by <see cref="RowShapeWriter"/>: <c>POST record</c> checks and stores a row, <c>GET paid</c> works out
/// what a document has been paid. The table's own CRUD routes stay the read-only ones of a log table. Amounts are added in memory, as decimals, so a SQLite database (which holds a decimal as text) answers as the others do. </summary>
public static class LedgerWriter
{
    public static string RecordMapLine(LedgerShape s) =>
        $"        app.MapPost(\"§R/{LedgerPlan.RecordRoute}\", ([FromServices] §CTX context, §T row) => RecordAsync(context, row))§AUTH;\n";

    public static string PaidMapLine(LedgerShape s) =>
        $"        app.MapGet(\"§R/{LedgerPlan.PaidRoute}\", ([FromServices] §CTX context, [FromQuery(Name = \"document\")] {s.Document.CSharpBase()}? document) => PaidAsync(context, document))§AUTH;\n";

    public static string Code(LedgerShape s, string contextType, ColumnModel? stamp = null)
    {
        string stampLine = stamp is { } c && c.CSharpBase() == "DateTime" ? $"        if (row.{c.CSharpName()} == default)\n            row.{c.CSharpName()} = DateTime.Now;\n" : "";
        string table = s.Table.TableName, documents = s.DocumentTable.TableName, doc = s.Document.CSharpName(), kind = s.Kind.CSharpName(), status = s.Status.CSharpName(), amount = s.Amount.CSharpName();
        string gateway = s.GatewayId.CSharpName(), key = s.IdempotencyKey.CSharpName(), documentKey = s.DocumentKey.CSharpName(), total = s.Total.CSharpName(), rowKey = s.Key.CSharpName(), documentType = s.Document.CSharpBase();
        return $$"""
    /// <summary> The kind and status are one of the ledger's words (written in lower case), the document exists, a refund is a negative amount and any other kind a positive one, the gateway id and the idempotency key are given and a gateway id is not recorded yet,
    /// a retry (an idempotency key recorded for the same document, kind and amount) answers with the stored row, the key of a different transaction is refused, a succeeded refund is no more than what has been paid so far and a succeeded
    /// capture does not take what is paid above the total of the document. </summary>
    public static async Task<IResult?> CheckLedgerAsync({{contextType}} context, {{table}} row)
    {
        row.{{kind}} = (row.{{kind}} ?? "").Trim().ToLowerInvariant();
        row.{{status}} = (row.{{status}} ?? "").Trim().ToLowerInvariant();
        row.{{gateway}} = (row.{{gateway}} ?? "").Trim();
        row.{{key}} = (row.{{key}} ?? "").Trim();
        if (row.{{kind}} is not ("{{LedgerPlan.Authorize}}" or "{{LedgerPlan.Capture}}" or "{{LedgerPlan.Refund}}" or "{{LedgerPlan.Void}}"))
            return RowShapeProblems.PaymentInvalid("{{PaymentWords.KindBad}}");
        if (row.{{status}} is not ("{{LedgerPlan.Pending}}" or "{{LedgerPlan.Succeeded}}" or "{{LedgerPlan.Failed}}"))
            return RowShapeProblems.PaymentInvalid("{{PaymentWords.StatusBad}}");
        if (!await context.Set<{{documents}}>().AsNoTracking().AnyAsync(d => d.{{documentKey}} == row.{{doc}}))
            return RowShapeProblems.PaymentInvalid("{{PaymentWords.DocumentMissing}}");
        decimal amount = (decimal)row.{{amount}};
        if (row.{{kind}} == "{{LedgerPlan.Refund}}" ? amount >= 0 : amount <= 0)
            return RowShapeProblems.PaymentInvalid(row.{{kind}} == "{{LedgerPlan.Refund}}" ? "{{PaymentWords.RefundSign}}" : "{{PaymentWords.AmountPositive}}");
        if (row.{{gateway}}.Length == 0 || row.{{key}}.Length == 0)
            return RowShapeProblems.PaymentInvalid("{{PaymentWords.GatewayMissing}}");
        var again = await context.Set<{{table}}>().AsNoTracking().FirstOrDefaultAsync(x => x.{{key}} == row.{{key}});
        if (again is not null)
            return again.{{doc}} == row.{{doc}} && again.{{kind}} == row.{{kind}} && (decimal)again.{{amount}} == amount ? Results.Ok(again) : RowShapeProblems.LedgerDuplicate("{{PaymentWords.KeyReused}}");
        if (await context.Set<{{table}}>().AsNoTracking().AnyAsync(x => x.{{gateway}} == row.{{gateway}}))
            return RowShapeProblems.LedgerDuplicate("{{PaymentWords.GatewayTaken}}");
        if (row.{{status}} == "{{LedgerPlan.Succeeded}}" && (row.{{kind}} == "{{LedgerPlan.Refund}}" || row.{{kind}} == "{{LedgerPlan.Capture}}"))
        {
            var held = await context.Set<{{table}}>().AsNoTracking().Where(x => x.{{doc}} == row.{{doc}} && x.{{status}} == "{{LedgerPlan.Succeeded}}" && (x.{{kind}} == "{{LedgerPlan.Capture}}" || x.{{kind}} == "{{LedgerPlan.Refund}}")).Select(x => x.{{amount}}).ToListAsync();
            decimal paid = held.Sum(x => (decimal)x) + amount;
            if (paid < 0)
                return RowShapeProblems.RefundOverPaid("{{PaymentWords.RefundOver}}");
            var limit = await context.Set<{{documents}}>().AsNoTracking().Where(d => d.{{documentKey}} == row.{{doc}}).Select(d => new { d.{{total}} }).FirstAsync();
            if (paid > (decimal)limit.{{total}})
                return RowShapeProblems.CaptureOverTotal("{{PaymentWords.CaptureOver}}");
        }
        return null;
    }

    /// <summary> Checks a row and stores it; the answer is the stored row. The key is the database's. </summary>
    public static async Task<IResult> RecordAsync({{contextType}} context, {{table}} row)
    {
        row.{{rowKey}} = default;
{{stampLine}}        if (await CheckLedgerAsync(context, row) is { } problem)
            return problem;
        context.Set<{{table}}>().Add(row);
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            context.Entry(row).State = EntityState.Detached;
            return RowShapeProblems.LedgerDuplicate("{{PaymentWords.GatewayTaken}}");
        }
        return Results.Created($"{{LedgerPlan.RecordRoute}}/{row.{{rowKey}}}", row);
    }

    /// <summary> What a document has been paid, worked out from the succeeded rows: the captured amount, the refunded amount, what is paid (captured less refunded), what is left of the total and whether the total is paid. </summary>
    public static async Task<IResult> PaidAsync({{contextType}} context, {{documentType}}? document)
    {
        if (document is null)
            return Results.Problem(statusCode: 400, title: "{{PaymentWords.NeedsDocument}}");
        var held = await context.Set<{{documents}}>().AsNoTracking().Where(d => d.{{documentKey}} == document).Select(d => new { d.{{total}} }).FirstOrDefaultAsync();
        if (held is null)
            return RowShapeProblems.PaymentInvalid("{{PaymentWords.DocumentMissing}}");
        var rows = await context.Set<{{table}}>().AsNoTracking().Where(x => x.{{doc}} == document && x.{{status}} == "{{LedgerPlan.Succeeded}}").Select(x => new { x.{{kind}}, x.{{amount}} }).ToListAsync();
        decimal total = (decimal)held.{{total}};
        decimal captured = rows.Where(x => x.{{kind}} == "{{LedgerPlan.Capture}}").Sum(x => (decimal)x.{{amount}});
        decimal refunded = -rows.Where(x => x.{{kind}} == "{{LedgerPlan.Refund}}").Sum(x => (decimal)x.{{amount}});
        decimal paid = captured - refunded;
        return Results.Ok(new { document, total, captured, refunded, paid, remaining = total - paid, settled = paid >= total, transactions = rows.Count });
    }


""";
    }
}
