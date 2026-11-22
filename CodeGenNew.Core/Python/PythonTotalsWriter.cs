using System.Globalization;
using System.Text;
using CodeGenNew.Core.Totals;

namespace CodeGenNew.Core.Python;

/// <summary> The Python (FastAPI) counterpart of <see cref="DocumentTotalsWriter"/> (<c>DocumentTotals</c>): the header's router module gets session events that note the documents a flush touches (the header, its lines
/// and its allowance rows) and, in the same flush and so the same transaction, write the worked-out totals into the header. <c>GET document-totals-check</c> lists the headers whose stored totals differ from their rows. </summary>
public static class PythonTotalsWriter
{
    /// <summary> A model class with its module, its key attribute and the attributes the totals read. </summary>
    public sealed record Table(string Class, string Module, string Key);

    /// <summary> <c>Parts</c> are the owned header attributes by part name (<see cref="DocumentTotalsWords"/>), <c>Annotations</c> their Python types (<c>Decimal</c> or <c>float</c>), <c>Scales</c> their scales. </summary>
    public sealed record Names(Table Header, string HeaderModule, Table Lines, string LineKey, string LineAmount, Table? Extra, string? ExtraDocument, string? ExtraFlag, string? ExtraAmount, string? Tax, string? Prepaid,
        string? Currency, IReadOnlyList<(string Part, string Attribute, string Annotation, int Scale)> Parts, IReadOnlyList<DocumentTotalsRound> Rounds, string Route, IReadOnlyList<DocumentTotalsPlaces>? Places = null, string? ExtraTax = null);

    /// <summary> The names of a header, its lines and its allowance table, or null without a plan. </summary>
    public static Names? NamesFor(DocumentTotalsShape? plan, PythonTable t, ProjectSettings project)
    {
        if (plan is null)
            return null;
        var lines = PythonTable.Of(plan.Lines, project);
        var extra = plan.Allowance is null ? null : PythonTable.Of(plan.Allowance.Table, project);
        var parts = plan.Columns.Owned().Select(p => (p.Part, t.SchemaFieldOf(p.Column)!.Name, t.SchemaFieldOf(p.Column)!.Annotation, p.Column.Scale ?? DocumentTotalsWords.DefaultScale)).ToList();
        return new Names(new Table(t.Class, t.Module, t.Key.Name), t.Module, new Table(lines.Class, lines.Module, lines.Key.Name), lines.FieldOf(plan.LineKey).Name, lines.FieldOf(plan.LineAmount).Name,
            extra is null ? null : new Table(extra.Class, extra.Module, extra.Key.Name), extra?.FieldOf(plan.Allowance!.Document!).Name, extra?.FieldOf(plan.Allowance!.Flag).Name,
            extra?.FieldOf(plan.Allowance!.Amount).Name, plan.Tax is null ? null : lines.FieldOf(plan.Tax.Tax).Name, plan.Columns.Prepaid is null ? null : t.SchemaFieldOf(plan.Columns.Prepaid)!.Name,
            plan.Currency is null ? null : t.SchemaFieldOf(plan.Currency)!.Name, parts, plan.Rounds, t.Route, plan.Places, plan.AllowanceTax is null || extra is null ? null : extra.FieldOf(plan.AllowanceTax).Name);
    }

    private static string Back(string annotation, string value) => annotation == "Decimal" ? value : $"float({value})";

    private static string Decimal(decimal value) => "Decimal(\"" + value.ToString(CultureInfo.InvariantCulture) + "\")";

    private static int ScaleOf(Names n, string part) => n.Parts.FirstOrDefault(p => p.Part == part).Scale is var scale && scale > 0 ? scale : DocumentTotalsWords.DefaultScale;

    public static string Module(Names n)
    {
        string header = n.Header.Class, key = n.Header.Key;
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        L("# DocumentTotals: the totals of a document are worked out again from its rows in every flush that touches the header, its lines or its allowance rows, inside the same transaction.");
        L($"TOTALS_KEY = \"totals_{n.Header.Module}\"");
        var (specificRounds, anyRound) = DocumentTotalsCurrencyRules.Split(n.Rounds);
        var (specificPlaces, anyPlaces) = DocumentTotalsCurrencyRules.Split(n.Places);
        L($"TOTALS_ROUNDS = {{{string.Join(", ", specificRounds.Select(r => $"\"{r.Currency.ToUpperInvariant()}\": {Decimal(r.Increment)}"))}}}");
        L($"TOTALS_ROUND_ANY = {(anyRound is not null ? Decimal(anyRound.Increment) : "None")}");
        L($"TOTALS_PLACES = {{{string.Join(", ", specificPlaces.Select(r => $"\"{r.Currency.ToUpperInvariant()}\": {r.Places}"))}}}");
        L($"TOTALS_PLACES_ANY = {(anyPlaces is not null ? anyPlaces.Places.ToString(CultureInfo.InvariantCulture) : "None")}");
        L();
        L();
        L("def money(value, scale):");
        L("    return Decimal(str(value)).quantize(Decimal(1).scaleb(-scale), rounding=ROUND_HALF_UP)");
        L();
        L();
        L("def totals_places(currency, scale):");
        L("    \"\"\"The decimal places of an amount in the currency: the places of the currency, at most the scale of the column that holds the amount.\"\"\"");
        L("    places = TOTALS_PLACES.get((currency or \"\").strip().upper(), TOTALS_PLACES_ANY)");
        L("    return scale if places is None else min(places, scale)");
        L();
        L();
        L("def work_out_totals(lines, allowances, charges, tax, prepaid, currency):");
        L("    \"\"\"The totals from the sums of the rows: every amount is rounded half away from zero to the decimal places of its currency (at most the scale of its column) and the next one is worked out from the rounded ones.\"\"\"");
        L($"    lines = money(lines, totals_places(currency, {ScaleOf(n, DocumentTotalsWords.PartLines)}))");
        L($"    allowances = money(allowances, totals_places(currency, {ScaleOf(n, DocumentTotalsWords.PartAllowances)}))");
        L($"    charges = money(charges, totals_places(currency, {ScaleOf(n, DocumentTotalsWords.PartCharges)}))");
        L($"    exclusive = money(lines - allowances + charges, totals_places(currency, {ScaleOf(n, DocumentTotalsWords.PartTaxExclusive)}))");
        L($"    tax = money(tax, totals_places(currency, {ScaleOf(n, DocumentTotalsWords.PartTax)}))");
        L($"    inclusive = money(exclusive + tax, totals_places(currency, {ScaleOf(n, DocumentTotalsWords.PartTaxInclusive)}))");
        L("    due = inclusive - Decimal(str(prepaid or 0))");
        L("    step = TOTALS_ROUNDS.get((currency or \"\").strip().upper(), TOTALS_ROUND_ANY)");
        L($"    rounding = money((due / step).quantize(Decimal(1), rounding=ROUND_HALF_UP) * step - due, totals_places(currency, {ScaleOf(n, DocumentTotalsWords.PartRounding)})) if step else Decimal(0)");
        L($"    return {{\"Lines\": lines, \"Allowances\": allowances, \"Charges\": charges, \"TaxExclusive\": exclusive, \"Tax\": tax, \"TaxInclusive\": inclusive, \"Rounding\": rounding, \"Payable\": money(due + rounding, totals_places(currency, {ScaleOf(n, DocumentTotalsWords.PartPayable)}))}}");
        L();
        L();
        L("def worked_out_totals(session, key):");
        L("    \"\"\"The totals of one document from its rows, or None when the document is not there.\"\"\"");
        string headerColumns = string.Join(", ", new[] { n.Prepaid is null ? null : $"{header}.{n.Prepaid}", n.Currency is null ? null : $"{header}.{n.Currency}" }.OfType<string>());
        if (headerColumns.Length > 0)
        {
            L($"    found = session.execute(select({headerColumns}).where({header}.{key} == key)).first()");
            L("    if found is None:");
            L("        return None");
            L($"    prepaid = {(n.Prepaid is null ? "0" : "found[0]")}");
            L($"    currency = {(n.Currency is null ? "None" : n.Prepaid is null ? "found[0]" : "found[1]")}");
        }
        else
        {
            L($"    if session.scalar(select({header}.{key}).where({header}.{key} == key)) is None:");
            L("        return None");
            L("    prepaid, currency = 0, None");
        }
        L($"    lines = sum((Decimal(str(v or 0)) for v in session.scalars(select({n.Lines.Class}.{n.LineAmount}).where({n.Lines.Class}.{n.LineKey} == key))), Decimal(0))");
        if (n.Extra is not null)
        {
            string withCategory = n.ExtraTax is null ? "" : $", {n.Extra.Class}.{n.ExtraTax}";
            L($"    extras = session.execute(select({n.Extra.Class}.{n.ExtraFlag}, {n.Extra.Class}.{n.ExtraAmount}{withCategory}).where({n.Extra.Class}.{n.ExtraDocument} == key)).all()");
            L("    allowances = sum((Decimal(str(row[1] or 0)) for row in extras if not row[0]), Decimal(0))");
            L("    charges = sum((Decimal(str(row[1] or 0)) for row in extras if row[0]), Decimal(0))");
        }
        else
            L("    allowances = charges = Decimal(0)");
        if (n.Tax is not null)
            L($"    tax = sum((Decimal(str(v or 0)) for v in session.scalars(select({n.Lines.Class}.{n.Tax}).where({n.Lines.Class}.{n.LineKey} == key))), Decimal(0))");
        else
            L("    tax = Decimal(0)");
        if (n.ExtraTax is not null && n.Extra is not null)
            L("    tax += sum(((1 if row[0] else -1) * Decimal(str(row[2] or 0)) for row in extras), Decimal(0))");
        L("    return work_out_totals(lines, allowances, charges, tax, prepaid, currency)");
        L();
        L();
        var owned = n.Parts.ToList();
        L("def lock_totals(session, key):");
        L("    \"\"\"Updates the header row of a document first, before the flush writes a row of it: the foreign key check of a line holds a shared lock on its header, and two flushes that each held one and then asked for the update lock would deadlock.\"\"\"");
        L($"    lock = sql_update({header}).where({header}.{key} == key).values({owned[0].Attribute}={header}.{owned[0].Attribute}).execution_options(synchronize_session=False)");
        L("    return session.execute(lock).rowcount");
        L();
        L();
        L("def write_totals(session, key):");
        L("    \"\"\"Writes the totals of one document into its header. The header row is updated first, so a second flush of the same document waits for the first to commit before it reads the rows.\"\"\"");
        L("    if lock_totals(session, key) == 0:");
        L("        return");
        L("    worked = worked_out_totals(session, key)");
        L("    if worked is None:");
        L("        return");
        L($"    values = {{{string.Join(", ", owned.Select(p => $"\"{p.Attribute}\": {Back(p.Annotation, $"worked[\"{p.Part}\"]")}"))}}}");
        L($"    session.execute(sql_update({header}).where({header}.{key} == key).values(**values).execution_options(synchronize_session=False))");
        L();
        L();
        L("@event.listens_for(Session, \"before_flush\")");
        L("def note_totals(session, flush_context, instances):");
        L("    keys = session.info.setdefault(TOTALS_KEY, ([], set()))");
        L("    headers, ids = keys");
        L("    for row in list(session.new) + list(session.dirty) + list(session.deleted):");
        L($"        if isinstance(row, {n.Lines.Class}):");
        L($"            attribute = (row.{n.LineKey}, inspect(row).attrs.{n.LineKey}.history.deleted)");
        L("            ids.update(value for value in (attribute[0], *attribute[1]) if value is not None)");
        if (n.Extra is not null)
        {
            L($"        elif isinstance(row, {n.Extra.Class}):");
            L($"            attribute = (row.{n.ExtraDocument}, inspect(row).attrs.{n.ExtraDocument}.history.deleted)");
            L("            ids.update(value for value in (attribute[0], *attribute[1]) if value is not None)");
        }
        L($"        elif isinstance(row, {header}) and row not in session.deleted:");
        L("            headers.append(row)");
        L("            if row in session.new:");
        foreach (var p in owned)
            L($"                row.{p.Attribute} = {Back(p.Annotation, "Decimal(0)")}");
        L("    for key in sorted(ids):");
        L("        lock_totals(session, key)");
        L();
        L();
        L("@event.listens_for(Session, \"after_flush\")");
        L("def write_all_totals(session, flush_context):");
        L("    headers, ids = session.info.pop(TOTALS_KEY, ([], set()))");
        L($"    ids.update(row.{key} for row in headers if row.{key} is not None)");
        L("    for key in sorted(ids):");
        L("        write_totals(session, key)");
        L($"    for row in [r for r in session.identity_map.values() if isinstance(r, {header}) and r.{key} in ids]:");
        L("        session.expire(row)");
        return o.ToString();
    }

    /// <summary> The check route, written before the routes with a key so that <c>document-totals-check</c> is not read as a key. </summary>
    public static string Route(Names n, string userParameter = "", string scopeLines = "")
    {
        string header = n.Header.Class, key = n.Header.Key;
        var owned = n.Parts.ToList();
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        L($"@router.get(\"{n.Route}/{DocumentTotalsWords.CheckRoute}\")");
        L($"def document_totals_check(id: int | None = Query(None), session: Session = Depends(get_session){userParameter}):");
        L("    \"\"\"The headers whose stored totals differ from their rows (one header when id is given).\"\"\"");
        L($"    statement = select({header}).order_by({header}.{key}).limit({DocumentTotalsWords.CheckLimit})");
        o.Append(scopeLines);
        L("    if id is not None:");
        L($"        statement = statement.where({header}.{key} == id)");
        L("    differing = []");
        L("    rows = session.scalars(statement).all()");
        L("    for row in rows:");
        L($"        worked = worked_out_totals(session, row.{key})");
        L("        if worked is None:");
        L("            continue");
        L("        parts = []");
        foreach (var p in owned)
            L($"        if abs(Decimal(str(row.{p.Attribute} or 0)) - worked[\"{p.Part}\"]) >= Decimal(\"0.5\").scaleb(-{p.Scale}):\n            parts.append({{\"part\": \"{p.Part}\", \"stored\": Decimal(str(row.{p.Attribute} or 0)), \"expected\": worked[\"{p.Part}\"]}})");
        L($"        if parts:\n            differing.append({{\"id\": row.{key}, \"parts\": parts}})");
        L("    return {\"checked\": len(rows), \"differing\": len(differing), \"headers\": differing}");
        return o.ToString();
    }
}
