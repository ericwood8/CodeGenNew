using CodeGenNew.Core.Payment;

namespace CodeGenNew.Core.Python;

/// <summary> The Python (FastAPI) counterpart of the payment part of <see cref="RowShapeWriter"/> (<c>BankAccountTables</c>, <c>InstallmentTables</c>, <c>PaymentTermsTables</c>): the table's router gets <c>check_account</c>,
/// <c>check_installment</c> or <c>check_due</c>, called before a create or an update commits, and <c>GET schedule</c> on an installment table. The words are <see cref="PaymentWords"/>. </summary>
public static class PythonPaymentWriter
{
    public enum Kind { Account, Installment, Due }

    /// <summary> The Python names one payment table needs. <paramref name="Related"/> are the tables whose model the helper reads (the document, the terms). </summary>
    public sealed record Names(Kind Kind, string Class, string Key, string? Iban, bool IbanNullable, string? Account, bool AccountNullable, string? Routing, bool RoutingNullable, string? Document, string? Sequence, string? Due,
        string? DueAnnotation, string? Amount, string? Terms, PythonTable? DocumentTable, string? DocumentKey, string? DocumentValue, string? DocumentAnnotation, PythonTable? TermsTable, string? DaysName, string? KeyAnnotation = null, string? EarlyPercent = null, string? EarlyDays = null, string? LatePercent = null, string? TotalName = null, string? TotalUnit = null)
    {
        /// <summary> Whether the router has a route of its own (<c>schedule</c> or <c>payable</c>). </summary>
        public bool HasRoute => Kind == Kind.Installment || Kind == Kind.Due && TotalName is not null;

        /// <summary> Whether the helper or the route uses <c>Decimal</c>. </summary>
        public bool UsesDecimal => Kind == Kind.Installment || Kind == Kind.Due && TotalName is not null;

        public IEnumerable<PythonTable> Related => new[] { DocumentTable, TermsTable }.OfType<PythonTable>().DistinctBy(t => t.Class).Where(t => t.Class != Class);
    }

    public static Names? NamesFor(BankAccountShape? bank, InstallmentShape? installment, DueDateShape? due, PythonTable t, ProjectSettings project)
    {
        if (bank is not null)
        {
            var iban = bank.Iban is null ? null : t.FieldOf(bank.Iban);
            var account = bank.Account is null ? null : t.FieldOf(bank.Account);
            var routing = bank.Routing is null ? null : t.FieldOf(bank.Routing);
            return new Names(Kind.Account, t.Class, t.Key.Name, iban?.Name, bank.Iban?.IsNullable ?? false, account?.Name, bank.Account?.IsNullable ?? false, routing?.Name, bank.Routing?.IsNullable ?? false, null, null, null, null, null, null, null, null, null, null, null, null);
        }
        if (installment is not null)
        {
            var documents = PythonTable.Of(installment.DocumentTable, project);
            return new Names(Kind.Installment, t.Class, t.Key.Name, null, false, null, false, null, false, t.FieldOf(installment.Document).Name, t.FieldOf(installment.Sequence).Name, t.FieldOf(installment.Due).Name, t.FieldOf(installment.Due).Annotation,
                t.FieldOf(installment.Amount).Name, null, documents, documents.FieldOf(installment.DocumentKey).Name, documents.FieldOf(installment.Total).Name, t.FieldOf(installment.Document).Annotation, null, null);
        }
        if (due is not null)
        {
            var documents = PythonTable.Of(due.DocumentTable, project);
            var terms = PythonTable.Of(due.TermsTable, project);
            return new Names(Kind.Due, t.Class, t.Key.Name, null, false, null, false, null, false, t.FieldOf(due.Document).Name, null, t.FieldOf(due.Due).Name, t.FieldOf(due.Due).Annotation, null, t.FieldOf(due.Terms).Name, documents,
                documents.FieldOf(due.DocumentKey).Name, documents.FieldOf(due.DocumentDate).Name, null, terms, terms.FieldOf(due.Days).Name, t.Key.Annotation,
                due.EarlyPercent is null ? null : terms.FieldOf(due.EarlyPercent).Name, due.EarlyPercent is null ? null : terms.FieldOf(due.EarlyDays!).Name, due.LatePercent is null ? null : terms.FieldOf(due.LatePercent).Name,
                due.CanPay ? documents.FieldOf(due.Total!).Name : null, due.Total is null ? null : "0." + new string('0', Math.Max(0, (due.Total.Scale ?? 2) - 1)) + "1");
        }
        return null;
    }

    /// <summary> The Python words the helper needs from <c>datetime</c>. </summary>
    public static string[] DateWords(Names n) => n.Kind == Kind.Due ? ["date", "datetime", "time", "timedelta"] : [];

    /// <summary> The helper and the check of the table, written before its routes. </summary>
    public static string Helper(Names n) => n.Kind switch
    {
        Kind.Account => AccountHelper(n),
        Kind.Installment => Fill(InstallmentHelper, n),
        _ => Fill(DueHelper, n),
    };

    public static string CheckLine(Names n) => n.Kind switch
    {
        Kind.Account => "    check_account(row)",
        Kind.Installment => "    check_installment(session, row)",
        _ => "    check_due(session, row)",
    };

    /// <summary> <c>GET schedule</c> of an installment table. </summary>
    public static string Route(Names n, string route, string readDepends, string userParameter) =>
        n.Kind == Kind.Due ? PayableRoute(n, route, readDepends, userParameter) : Fill(RouteText, n, ("§ROUTE", route), ("§READ", readDepends), ("§USER", userParameter));

    private static string PayableRoute(Names n, string route, string readDepends, string userParameter)
    {
        string early = n.EarlyPercent is null ? "" : TemplateText.Fill("""
    if terms is not None and terms.§EPCT is not None and terms.§EDAYS is not None and start is not None and day <= start + timedelta(days=terms.§EDAYS):
        discount = (total * Decimal(str(terms.§EPCT)) / 100).quantize(unit, ROUND_HALF_UP)
        rule = "§RULEEARLY"

""", ("§EPCT", n.EarlyPercent), ("§EDAYS", n.EarlyDays!), ("§RULEEARLY", PaymentWords.RuleEarly));
        string late = n.LatePercent is null ? "" : TemplateText.Fill("""
    §ELIF terms is not None and terms.§LPCT is not None and row.§DUE is not None and day > to_day(row.§DUE):
        penalty = (total * Decimal(str(terms.§LPCT)) / 100).quantize(unit, ROUND_HALF_UP)
        rule = "§RULELATE"

""", ("§ELIF", n.EarlyPercent is null ? "if" : "elif"), ("§LPCT", n.LatePercent), ("§DUE", n.Due!), ("§RULELATE", PaymentWords.RuleLate));
        return Fill(PayableText, n, ("§ROUTE", route), ("§READ", readDepends), ("§USER", userParameter), ("§KEYTYPE", n.KeyAnnotation ?? "int"), ("§UNIT", n.TotalUnit ?? "0.01"), ("§EARLYBLOCK", early + late),
            ("§W_NEEDSPAY", PaymentWords.NeedsPayment), ("§W_PAYMISSING", PaymentWords.PaymentMissing), ("§RULEONTIME", PaymentWords.RuleOnTime), ("§TOTALNAME", n.TotalName!));
    }

    private static string Store(bool nullable, string expression) => nullable ? expression : expression + " or \"\"";

    private static string AccountHelper(Names n)
    {
        var o = new System.Text.StringBuilder();
        o.Append(TemplateText.Fill(AccountText, ("§LENGTHS", PaymentPlan.IbanLengths), ("§INVALIDTITLE", PaymentWords.InvalidTitle), ("§INVALIDTYPE", PaymentWords.InvalidType)));
        o.Append("def check_account(row) -> None:\n    \"\"\"The IBAN is written without spaces and in upper case and must be valid; an account number with a routing number must be valid too; one of the two is needed.\"\"\"\n");
        if (n.Iban is not null)
        {
            o.Append($"    iban = \"\".join(c for c in (row.{n.Iban} or \"\") if c not in \" -\").upper() or None\n");
            o.Append($"    row.{n.Iban} = {Store(n.IbanNullable, "iban")}\n");
            o.Append($"    if iban is not None and not iban_valid(iban):\n        raise payment_invalid(\"{PaymentWords.IbanBad}\")\n");
        }
        else
            o.Append("    iban = None\n");
        if (n.Account is not null && n.Routing is not null)
        {
            o.Append($"    account = (row.{n.Account} or \"\").strip() or None\n    routing = (row.{n.Routing} or \"\").strip() or None\n");
            o.Append($"    row.{n.Account} = {Store(n.AccountNullable, "account")}\n    row.{n.Routing} = {Store(n.RoutingNullable, "routing")}\n");
            o.Append($"    if routing is not None and not routing_valid(routing):\n        raise payment_invalid(\"{PaymentWords.RoutingBad}\")\n");
            o.Append($"    if account is not None and routing is not None and not (4 <= len(account) <= 17 and account.isascii() and account.isdigit()):\n        raise payment_invalid(\"{PaymentWords.AccountNumberBad}\")\n");
            o.Append($"    if iban is None and (account is None or routing is None):\n        raise payment_invalid(\"{PaymentWords.AccountMissing}\")\n");
        }
        else
            o.Append($"    if iban is None:\n        raise payment_invalid(\"{PaymentWords.AccountMissing}\")\n");
        return o.ToString();
    }

    private static string Fill(string text, Names n, params (string Token, string Value)[] more)
    {
        var values = new List<(string, string)>
        {
            ("§CLASS", n.Class), ("§KEY", n.Key), ("§DOCCLASS", n.DocumentTable?.Class ?? ""), ("§DOCKEY", n.DocumentKey ?? ""), ("§DOCVALUE", n.DocumentValue ?? ""), ("§DOCTYPE", n.DocumentAnnotation ?? ""),
            ("§DOC", n.Document ?? ""), ("§SEQ", n.Sequence ?? ""), ("§DUEANN", n.DueAnnotation ?? ""), ("§DUE", n.Due ?? ""), ("§AMT", n.Amount ?? ""), ("§TERMSCLASS", n.TermsTable?.Class ?? ""), ("§TERMS", n.Terms ?? ""), ("§DAYS", n.DaysName ?? ""),
            ("§INVALIDTITLE", PaymentWords.InvalidTitle), ("§INVALIDTYPE", PaymentWords.InvalidType), ("§OVERTITLE", PaymentWords.OverTotalTitle), ("§OVERTYPE", PaymentWords.OverTotalType),
            ("§W_DOCUMENT", PaymentWords.DocumentMissing), ("§W_SEQ", PaymentWords.SequenceNotPositive), ("§W_AMOUNT", PaymentWords.AmountNotPositive), ("§W_TAKEN", PaymentWords.SequenceTaken), ("§W_ORDER", PaymentWords.OutOfOrder),
            ("§W_OVER", PaymentWords.OverTotal), ("§W_NEEDS", PaymentWords.NeedsDocument), ("§W_TERMS", PaymentWords.TermsMissing), ("§W_BEFORE", PaymentWords.DueBeforeDocument),
        };
        values.AddRange(more);
        return TemplateText.Fill(text, [.. values]);
    }

    private const string AccountText = """"
IBAN_LENGTHS = {code[:2]: int(code[2:]) for code in "§LENGTHS".split()}


def iban_valid(iban: str) -> bool:
    """An IBAN has the length of its country, and the number of its text with the first four characters moved to the end and the letters written as 10 to 35 leaves 1 when divided by 97."""
    if len(iban) < 5 or not iban.isascii() or not iban.isalnum() or iban[:2] not in IBAN_LENGTHS or len(iban) != IBAN_LENGTHS[iban[:2]] or not iban[:2].isalpha() or not iban[2:4].isdigit():
        return False
    return int("".join(str(int(c, 36)) for c in iban[4:] + iban[:4])) % 97 == 1


def routing_valid(routing: str) -> bool:
    """A routing number has nine digits and 3 * (1st + 4th + 7th) + 7 * (2nd + 5th + 8th) + (3rd + 6th + 9th) is a multiple of ten."""
    if len(routing) != 9 or not routing.isascii() or not routing.isdigit():
        return False
    d = [int(c) for c in routing]
    return (3 * (d[0] + d[3] + d[6]) + 7 * (d[1] + d[4] + d[7]) + (d[2] + d[5] + d[8])) % 10 == 0


def payment_invalid(detail: str) -> ApiError:
    return ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail=detail)


"""";

    private const string PayableText = """"
@router.get("§ROUTE/payable"§READ)
def payable_on(id: §KEYTYPE | None = Query(None), on: date | None = Query(None), session: Session = Depends(get_session)§USER):
    """What is payable on a day (today without one): the total less the early-payment discount while the discount days of the terms last, plus the late penalty once the due day has passed."""
    if id is None:
        raise ApiError(400, "§W_NEEDSPAY")
    row = session.get(§CLASS, id)
    if row is None:
        raise ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail="§W_PAYMISSING")
    document = session.get(§DOCCLASS, row.§DOC)
    if document is None:
        raise ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail="§W_DOCUMENT")
    terms = session.get(§TERMSCLASS, row.§TERMS) if row.§TERMS is not None else None
    total = Decimal(str(document.§TOTALNAME))
    day = on if on is not None else date.today()
    start = to_day(document.§DOCVALUE) if document.§DOCVALUE is not None else None
    unit = Decimal("§UNIT")
    discount = penalty = Decimal(0)
    rule = "§RULEONTIME"
§EARLYBLOCK    payable = total - discount + penalty
    return {"id": id, "document": row.§DOC, "total": float(total), "day": day.isoformat(), "dueDate": to_day(row.§DUE).isoformat() if row.§DUE is not None else None, "discount": float(discount), "penalty": float(penalty), "payable": float(payable), "rule": rule}
"""";

    private const string InstallmentHelper = """"
def check_installment(session: Session, row) -> None:
    """The document exists, the sequence number and the amount are above zero, no other installment of the document has the number, the due dates follow the numbers, and the installments add up to no more than the total of the document."""
    with session.no_autoflush:
        document = session.get(§DOCCLASS, row.§DOC)
        if document is None:
            raise ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail="§W_DOCUMENT")
        if row.§SEQ is None or row.§SEQ <= 0:
            raise ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail="§W_SEQ")
        if row.§AMT is None or Decimal(str(row.§AMT)) <= 0:
            raise ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail="§W_AMOUNT")
        statement = select(§CLASS.§SEQ, §CLASS.§DUE, §CLASS.§AMT).where(§CLASS.§DOC == row.§DOC)
        if row.§KEY is not None:
            statement = statement.where(§CLASS.§KEY != row.§KEY)
        others = session.execute(statement).all()
    if any(number == row.§SEQ for number, _, _ in others):
        raise ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail="§W_TAKEN")
    if any(number < row.§SEQ and due > row.§DUE or number > row.§SEQ and due < row.§DUE for number, due, _ in others):
        raise ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail="§W_ORDER")
    if sum((Decimal(str(amount)) for _, _, amount in others), Decimal(0)) + Decimal(str(row.§AMT)) > Decimal(str(document.§DOCVALUE)):
        raise ApiError(400, "§OVERTITLE", problem_type="§OVERTYPE", detail="§W_OVER")
"""";

    private const string DueHelper = """"
def to_day(value):
    return value.date() if isinstance(value, datetime) else value


def check_due(session: Session, row) -> None:
    """A row with payment terms and no due date gets the date of the document plus the days to pay; a due date before the date of the document is refused."""
    with session.no_autoflush:
        document = session.get(§DOCCLASS, row.§DOC)
        if document is None:
            raise ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail="§W_DOCUMENT")
        if document.§DOCVALUE is None:
            return
        start = to_day(document.§DOCVALUE)
        if row.§DUE is None:
            if row.§TERMS is None:
                return
            terms = session.get(§TERMSCLASS, row.§TERMS)
            if terms is None:
                raise ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail="§W_TERMS")
            day = start + timedelta(days=terms.§DAYS)
            row.§DUE = datetime.combine(day, time.min) if "§DUEANN" == "datetime" else day
            return
    if to_day(row.§DUE) < start:
        raise ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail="§W_BEFORE")
"""";

    private const string RouteText = """"
@router.get("§ROUTE/schedule"§READ)
def installment_schedule(document: §DOCTYPE | None = Query(None), session: Session = Depends(get_session)§USER):
    """What the installments of a document come to: the total, the sum scheduled, what is left to schedule and whether the schedule is complete."""
    if document is None:
        raise ApiError(400, "§W_NEEDS")
    held = session.get(§DOCCLASS, document)
    if held is None:
        raise ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail="§W_DOCUMENT")
    amounts = session.scalars(select(§CLASS.§AMT).where(§CLASS.§DOC == document)).all()
    total = Decimal(str(held.§DOCVALUE))
    scheduled = sum((Decimal(str(amount)) for amount in amounts), Decimal(0))
    return {"document": document, "total": float(total), "scheduled": float(scheduled), "remaining": float(total - scheduled), "complete": scheduled == total, "installments": len(amounts)}
"""";
}
