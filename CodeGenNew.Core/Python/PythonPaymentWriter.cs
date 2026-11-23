using CodeGenNew.Core.Payment;

namespace CodeGenNew.Core.Python;

/// <summary> The Python (FastAPI) counterpart of the payment part of <see cref="RowShapeWriter"/> (<c>BankAccountTables</c>, <c>InstallmentTables</c>, <c>PaymentTermsTables</c>): the table's router gets <c>check_account</c>,
/// <c>check_installment</c> or <c>check_due</c>, called before a create or an update commits, and <c>GET schedule</c> on an installment table. The words are <see cref="PaymentWords"/>. </summary>
public static class PythonPaymentWriter
{
    public enum Kind { Account, Installment, Due, Ledger }

    /// <summary> The Python names one payment table needs. <paramref name="Related"/> are the tables whose model the helper reads (the document, the terms). </summary>
    public sealed record Names(Kind Kind, string Class, string Key, string? Iban, bool IbanNullable, string? Account, bool AccountNullable, string? Routing, bool RoutingNullable, string? Document, string? Sequence, string? Due,
        string? DueAnnotation, string? Amount, string? Terms, PythonTable? DocumentTable, string? DocumentKey, string? DocumentValue, string? DocumentAnnotation, PythonTable? TermsTable, string? DaysName, string? KeyAnnotation = null, string? EarlyPercent = null, string? EarlyDays = null, string? LatePercent = null, string? TotalName = null, string? TotalUnit = null, LedgerFields? Ledger = null)
    {
        /// <summary> Whether the router has a route of its own (<c>schedule</c> or <c>payable</c>). </summary>
        public bool HasRoute => Kind == Kind.Installment || Kind == Kind.Ledger || Kind == Kind.Due && TotalName is not null;

        /// <summary> Whether the helper or the route uses <c>Decimal</c>. </summary>
        public bool UsesDecimal => Kind == Kind.Installment || Kind == Kind.Ledger || Kind == Kind.Due && TotalName is not null;

        public IEnumerable<PythonTable> Related => new[] { DocumentTable, TermsTable }.OfType<PythonTable>().DistinctBy(t => t.Class).Where(t => t.Class != Class);
    }

    /// <summary> The fields of a ledger row the helper and the routes read: the kind, the status, the amount, the gateway id, the idempotency key, and the time stamped on a row that has none. </summary>
    public sealed record LedgerFields(string KindName, string StatusName, string AmountName, string GatewayName, string KeyName, string? StampName, string? StampNow);

    public static Names? NamesFor(BankAccountShape? bank, InstallmentShape? installment, DueDateShape? due, PythonTable t, ProjectSettings project, LedgerShape? ledger = null)
    {
        if (ledger is not null)
        {
            var documents = PythonTable.Of(ledger.DocumentTable, project);
            var stamp = t.LogStamp;
            return new Names(Kind.Ledger, t.Class, t.Key.Name, null, false, null, false, null, false, t.FieldOf(ledger.Document).Name, null, null, null, t.FieldOf(ledger.Amount).Name, null, documents, documents.FieldOf(ledger.DocumentKey).Name,
                documents.FieldOf(ledger.Total).Name, t.FieldOf(ledger.Document).Annotation, null, null, t.Key.Annotation, null, null, null, documents.FieldOf(ledger.Total).Name, null,
                new LedgerFields(t.FieldOf(ledger.Kind).Name, t.FieldOf(ledger.Status).Name, t.FieldOf(ledger.Amount).Name, t.FieldOf(ledger.GatewayId).Name, t.FieldOf(ledger.IdempotencyKey).Name, stamp?.Name, stamp is null ? null : PythonTable.Now(stamp)));
        }
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
    public static string[] DateWords(Names n) => n.Kind == Kind.Due ? ["date", "datetime", "time", "timedelta"] : n.Kind == Kind.Ledger && n.Ledger?.StampNow is { } now ? [now.StartsWith("date.") ? "date" : "datetime"] : [];

    /// <summary> The helper and the check of the table, written before its routes. </summary>
    public static string Helper(Names n) => n.Kind switch
    {
        Kind.Account => AccountHelper(n),
        Kind.Installment => Fill(InstallmentHelper, n),
        Kind.Ledger => LedgerHelper(n),
        _ => Fill(DueHelper, n),
    };

    public static string CheckLine(Names n) => n.Kind switch
    {
        Kind.Account => "    check_account(row)",
        Kind.Installment => "    check_installment(session, row)",
        Kind.Ledger => "    check_ledger(session, row)",
        _ => "    check_due(session, row)",
    };

    /// <summary> <c>GET schedule</c> of an installment table. </summary>
    public static string Route(Names n, string route, string readDepends, string userParameter, string schema = "", string writeDepends = "") =>
        n.Kind == Kind.Ledger ? LedgerRoutes(n, route, readDepends, userParameter, schema, writeDepends) : n.Kind == Kind.Due ? PayableRoute(n, route, readDepends, userParameter) : Fill(RouteText, n, ("§ROUTE", route), ("§READ", readDepends), ("§USER", userParameter));

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

    private static string LedgerHelper(Names n) => Fill(LedgerHelperText, n, ("§KIND", n.Ledger!.KindName), ("§STATUS", n.Ledger.StatusName), ("§AMT", n.Ledger.AmountName), ("§GATEWAY", n.Ledger.GatewayName), ("§IDEM", n.Ledger.KeyName),
        ("§DUPTITLE", PaymentWords.LedgerDuplicateTitle), ("§DUPTYPE", PaymentWords.LedgerDuplicateType), ("§REFUNDTITLE", PaymentWords.RefundOverTitle), ("§REFUNDTYPE", PaymentWords.RefundOverType),
        ("§W_KIND", PaymentWords.KindBad), ("§W_STATUS", PaymentWords.StatusBad), ("§W_SIGN", PaymentWords.RefundSign), ("§W_POSITIVE", PaymentWords.AmountPositive), ("§W_GATEWAYMISSING", PaymentWords.GatewayMissing),
        ("§W_GATEWAYTAKEN", PaymentWords.GatewayTaken), ("§W_KEYREUSED", PaymentWords.KeyReused), ("§W_REFUNDOVER", PaymentWords.RefundOver),
        ("§CAPTURETITLE", PaymentWords.CaptureOverTitle), ("§CAPTURETYPE", PaymentWords.CaptureOverType), ("§W_CAPTUREOVER", PaymentWords.CaptureOver));

    private static string LedgerRoutes(Names n, string route, string readDepends, string userParameter, string schema, string writeDepends)
    {
        string stamp = n.Ledger!.StampName is null ? "" : $"    if row.{n.Ledger.StampName} is None:\n        row.{n.Ledger.StampName} = {n.Ledger.StampNow}\n";
        return Fill(LedgerRouteText, n, ("§ROUTE", route), ("§READ", readDepends), ("§USER", userParameter), ("§SCHEMA", schema), ("§WRITE", writeDepends), ("§STAMP", stamp), ("§KIND", n.Ledger.KindName), ("§STATUS", n.Ledger.StatusName), ("§AMT", n.Ledger.AmountName),
            ("§DUPTITLE", PaymentWords.LedgerDuplicateTitle), ("§DUPTYPE", PaymentWords.LedgerDuplicateType), ("§W_GATEWAYTAKEN", PaymentWords.GatewayTaken), ("§RECORD", LedgerPlan.RecordRoute), ("§PAID", LedgerPlan.PaidRoute),
            ("§STATUSOK", LedgerPlan.Succeeded), ("§CAPTURE", LedgerPlan.Capture), ("§REFUND", LedgerPlan.Refund), ("§UNIT", "0.01"));
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

    private const string LedgerHelperText = """"
LEDGER_KINDS = ("authorize", "capture", "refund", "void")
LEDGER_STATUSES = ("pending", "succeeded", "failed")


def ledger_invalid(detail: str) -> ApiError:
    return ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail=detail)


def check_ledger(session: Session, row):
    """The kind and status are one of the ledger's words (written in lower case), the document exists, a refund is a negative amount and any other kind a positive one, the gateway id and the idempotency key are given and a gateway id is not recorded yet, a retry (an idempotency key recorded for the same document, kind and amount) answers with the stored row, the key of a different transaction is refused, a succeeded refund is no more than what has been paid so far and a succeeded capture does not take what is paid above the total of the document."""
    row.§KIND = (row.§KIND or "").strip().lower()
    row.§STATUS = (row.§STATUS or "").strip().lower()
    row.§GATEWAY = (row.§GATEWAY or "").strip()
    row.§IDEM = (row.§IDEM or "").strip()
    if row.§KIND not in LEDGER_KINDS:
        raise ledger_invalid("§W_KIND")
    if row.§STATUS not in LEDGER_STATUSES:
        raise ledger_invalid("§W_STATUS")
    with session.no_autoflush:
        if session.get(§DOCCLASS, row.§DOC) is None:
            raise ledger_invalid("§W_DOCUMENT")
        amount = Decimal(str(row.§AMT))
        if (amount >= 0) if row.§KIND == "refund" else (amount <= 0):
            raise ledger_invalid("§W_SIGN" if row.§KIND == "refund" else "§W_POSITIVE")
        if not row.§GATEWAY or not row.§IDEM:
            raise ledger_invalid("§W_GATEWAYMISSING")
        again = session.scalar(select(§CLASS).where(§CLASS.§IDEM == row.§IDEM))
        if again is not None:
            if again.§DOC == row.§DOC and again.§KIND == row.§KIND and Decimal(str(again.§AMT)) == amount:
                return again
            raise ApiError(409, "§DUPTITLE", problem_type="§DUPTYPE", detail="§W_KEYREUSED")
        if session.scalar(select(§CLASS.§KEY).where(§CLASS.§GATEWAY == row.§GATEWAY)) is not None:
            raise ApiError(409, "§DUPTITLE", problem_type="§DUPTYPE", detail="§W_GATEWAYTAKEN")
        if row.§STATUS == "succeeded" and row.§KIND in ("capture", "refund"):
            held = session.scalars(select(§CLASS.§AMT).where(§CLASS.§DOC == row.§DOC, §CLASS.§STATUS == "succeeded", §CLASS.§KIND.in_(("capture", "refund")))).all()
            paid = sum((Decimal(str(value)) for value in held), Decimal(0)) + amount
            if paid < 0:
                raise ApiError(400, "§REFUNDTITLE", problem_type="§REFUNDTYPE", detail="§W_REFUNDOVER")
            if paid > Decimal(str(session.get(§DOCCLASS, row.§DOC).§DOCVALUE)):
                raise ApiError(400, "§CAPTURETITLE", problem_type="§CAPTURETYPE", detail="§W_CAPTUREOVER")
    return None
"""";

    private const string LedgerRouteText = """"
@router.post("§ROUTE/§RECORD", response_model=§SCHEMA, status_code=201§WRITE)
def record_transaction(body: §SCHEMA, response: Response, session: Session = Depends(get_session)§USER):
    """Checks a row and stores it; the answer is the stored row (a retry of a recorded transaction answers with the row stored the first time, 200)."""
    row = §CLASS(**body.columns())
    row.§KEY = None
§STAMP    existing = check_ledger(session, row)
    if existing is not None:
        response.status_code = 200
        return existing
    session.add(row)
    try:
        session.commit()
    except IntegrityError:
        session.rollback()
        raise ApiError(409, "§DUPTITLE", problem_type="§DUPTYPE", detail="§W_GATEWAYTAKEN")
    session.refresh(row)
    return row


@router.get("§ROUTE/§PAID"§READ)
def paid_amount(document: §DOCTYPE | None = Query(None), session: Session = Depends(get_session)§USER):
    """What a document has been paid, worked out from the succeeded rows: the captured amount, the refunded amount, what is paid (captured less refunded), what is left of the total and whether the total is paid."""
    if document is None:
        raise ApiError(400, "§W_NEEDS")
    held = session.get(§DOCCLASS, document)
    if held is None:
        raise ApiError(400, "§INVALIDTITLE", problem_type="§INVALIDTYPE", detail="§W_DOCUMENT")
    rows = session.execute(select(§CLASS.§KIND, §CLASS.§AMT).where(§CLASS.§DOC == document, §CLASS.§STATUS == "§STATUSOK")).all()
    total = Decimal(str(held.§DOCVALUE))
    captured = sum((Decimal(str(amount)) for kind, amount in rows if kind == "§CAPTURE"), Decimal(0))
    refunded = -sum((Decimal(str(amount)) for kind, amount in rows if kind == "§REFUND"), Decimal(0))
    paid = captured - refunded
    return {"document": document, "total": float(total), "captured": float(captured), "refunded": float(refunded), "paid": float(paid), "remaining": float(total - paid), "settled": paid >= total, "transactions": len(rows)}
"""";
}
