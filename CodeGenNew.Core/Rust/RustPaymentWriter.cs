using CodeGenNew.Core.Payment;
using CodeGenNew.Core.Sql;

namespace CodeGenNew.Core.Rust;

/// <summary> The Rust (axum, sqlx) counterpart of the payment part of <see cref="RowShapeWriter"/> (<c>BankAccountTables</c>, <c>InstallmentTables</c>, <c>PaymentTermsTables</c>): the table's router gets <c>check_account</c>,
/// <c>check_installment</c> or <c>check_due</c>, called before a create or an update writes the row, and <c>GET schedule</c> on an installment table. The words are <see cref="PaymentWords"/>. </summary>
public static class RustPaymentWriter
{
    public enum Kind { Account, Installment, Due, Ledger }

    /// <summary> The Rust names one payment table needs; the fields of the tables it reads come from their <see cref="RustTable"/>. </summary>
    public sealed record Names(Kind Kind, string Struct, string Table, string SelectList, string KeyColumn, string KeyType, string? IbanField, bool IbanOption, string? AccountField, bool AccountOption, string? RoutingField, bool RoutingOption,
        string? DocField, string? DocColumn, string? SeqField, string? DueField, bool DueOption, string? DueType, string? AmountField, string? AmountType, string? DocModule, string? DocKeyType, string? DocTotalField, string? DocDateField, string? DocDateType,
        string? TermsField, bool TermsOption, string? TermsModule, string? DaysField, string? DocDateOption, string? EarlyPercent = null, bool EarlyPercentOption = false, string? EarlyDays = null, bool EarlyDaysOption = false, string? LatePercent = null, bool LatePercentOption = false, string? TotalField = null, int Scale = 2, LedgerFields? Ledger = null)
    {
        /// <summary> Whether the router has a route of its own (<c>schedule</c> or <c>payable</c>). </summary>
        public bool HasRoute => Kind == Kind.Installment || Kind == Kind.Ledger || Kind == Kind.Due && TotalField is not null;

        public bool IsFloat => AmountType is "f32" or "f64";
    }

    /// <summary> The fields and quoted columns of a ledger row the checks and routes read, and the field stamped with the time on a row that has none. </summary>
    public sealed record LedgerFields(string KindField, string StatusField, string AmountField, string GatewayField, string KeyField, string KindColumn, string StatusColumn, string GatewayColumn, string KeyColumn, string RowKey, string? StampField, bool StampOption, int Scale);

    public static Names? NamesFor(BankAccountShape? bank, InstallmentShape? installment, DueDateShape? due, RustTable t, ProjectSettings project, DialectInfo dialect, string keyType, LedgerShape? ledger = null)
    {
        string key = dialect.Quote(t.Key.Column.DbName);
        if (ledger is not null)
        {
            var documents = RustTable.Of(ledger.DocumentTable, project);
            var stamp = t.LogStamp is { BaseType: "NaiveDateTime" or "chrono::NaiveDateTime" } logStamp ? logStamp : null;
            return new Names(Kind.Ledger, t.Struct, t.Table, t.SelectList, key, keyType, null, false, null, false, null, false, t.FieldOf(ledger.Document).Name, dialect.Quote(ledger.Document.DbName), null, null, false, null, t.FieldOf(ledger.Amount).Name,
                t.FieldOf(ledger.Amount).BaseType, documents.Module, documents.Key.Type, documents.FieldOf(ledger.Total).Name, null, null, null, false, null, null, null, null, false, null, false, null, false, documents.FieldOf(ledger.Total).Name, ledger.Total.Scale ?? 2,
                new LedgerFields(t.FieldOf(ledger.Kind).Name, t.FieldOf(ledger.Status).Name, t.FieldOf(ledger.Amount).Name, t.FieldOf(ledger.GatewayId).Name, t.FieldOf(ledger.IdempotencyKey).Name, dialect.Quote(ledger.Kind.DbName), dialect.Quote(ledger.Status.DbName),
                    dialect.Quote(ledger.GatewayId.DbName), dialect.Quote(ledger.IdempotencyKey.DbName), t.Key.Name, stamp?.Name, stamp?.IsOption ?? false, ledger.Total.Scale ?? 2));
        }
        if (bank is not null)
            return new Names(Kind.Account, t.Struct, t.Table, t.SelectList, key, keyType, bank.Iban is null ? null : t.FieldOf(bank.Iban).Name, bank.Iban is not null && t.FieldOf(bank.Iban).IsOption,
                bank.Account is null ? null : t.FieldOf(bank.Account).Name, bank.Account is not null && t.FieldOf(bank.Account).IsOption, bank.Routing is null ? null : t.FieldOf(bank.Routing).Name, bank.Routing is not null && t.FieldOf(bank.Routing).IsOption,
                null, null, null, null, false, null, null, null, null, null, null, null, null, null, false, null, null, null);
        if (installment is not null)
        {
            var documents = RustTable.Of(installment.DocumentTable, project);
            return new Names(Kind.Installment, t.Struct, t.Table, t.SelectList, key, keyType, null, false, null, false, null, false, t.FieldOf(installment.Document).Name, dialect.Quote(installment.Document.DbName), t.FieldOf(installment.Sequence).Name,
                t.FieldOf(installment.Due).Name, false, t.FieldOf(installment.Due).BaseType, t.FieldOf(installment.Amount).Name, t.FieldOf(installment.Amount).BaseType, documents.Module, documents.Key.Type, documents.FieldOf(installment.Total).Name, null, null, null, false, null, null, null);
        }
        if (due is not null)
        {
            var documents = RustTable.Of(due.DocumentTable, project);
            var terms = RustTable.Of(due.TermsTable, project);
            return new Names(Kind.Due, t.Struct, t.Table, t.SelectList, key, keyType, null, false, null, false, null, false, t.FieldOf(due.Document).Name, null, null, t.FieldOf(due.Due).Name, true, t.FieldOf(due.Due).BaseType, null, null, documents.Module,
                documents.Key.Type, null, documents.FieldOf(due.DocumentDate).Name, documents.FieldOf(due.DocumentDate).BaseType, t.FieldOf(due.Terms).Name, t.FieldOf(due.Terms).IsOption, terms.Module, terms.FieldOf(due.Days).Name, documents.FieldOf(due.DocumentDate).IsOption ? "option" : null,
                due.EarlyPercent is null ? null : terms.FieldOf(due.EarlyPercent).Name, due.EarlyPercent is not null && terms.FieldOf(due.EarlyPercent).IsOption, due.EarlyPercent is null ? null : terms.FieldOf(due.EarlyDays!).Name, due.EarlyDays is not null && terms.FieldOf(due.EarlyDays).IsOption,
                due.LatePercent is null ? null : terms.FieldOf(due.LatePercent).Name, due.LatePercent is not null && terms.FieldOf(due.LatePercent).IsOption, due.CanPay ? documents.FieldOf(due.Total!).Name : null, due.Total?.Scale ?? 2);
        }
        return null;
    }

    public static string RouteLine(string route, string readLayer, Names? n = null) => n?.Kind == Kind.Ledger ? $"        .route(\"{route}/{LedgerPlan.PaidRoute}\", get(paid_amount){readLayer})\n        .route(\"{route}/{LedgerPlan.RecordRoute}\", post(record_transaction){readLayer})\n" : n?.Kind == Kind.Due ? $"        .route(\"{route}/payable\", get(payable_on){readLayer})\n" : $"        .route(\"{route}/schedule\", get(installment_schedule){readLayer})\n";

    public static string CheckLine(Names n, bool update) => n.Kind switch
    {
        Kind.Account => "    check_account(&mut row)?;",
        Kind.Installment => $"    check_installment(&pool, {(update ? "Some(id)" : "None")}, &mut row).await?;",
        Kind.Ledger => "    check_ledger(&pool, &mut row).await?;",
        _ => "    check_due(&pool, &mut row).await?;",
    };

    public static string Code(Names n) => n.Kind switch
    {
        Kind.Account => AccountCode(n),
        Kind.Installment => InstallmentCode(n),
        Kind.Ledger => LedgerCode(n),
        _ => DueCode(n) + (n.TotalField is not null ? PayableCode(n) : ""),
    };

    private static string Invalid(string detail) => $"ApiError::Rejected(400, \"{PaymentWords.InvalidType}\", \"{PaymentWords.InvalidTitle}\", \"{detail}\".into())";

    private static string Read(string field, bool option) => option ? $"row.{field}.clone().unwrap_or_default()" : $"row.{field}.clone()";

    private static string Write(string field, bool option, string expression) => option ? $"    row.{field} = {expression}.clone();" : $"    row.{field} = {expression}.clone().unwrap_or_default();";

    private static string AccountCode(Names n)
    {
        var o = new System.Text.StringBuilder();
        o.Append(TemplateText.Fill(AccountText, ("§LENGTHS", PaymentPlan.IbanLengths), ("§STRUCT", n.Struct)));
        if (n.IbanField is not null)
        {
            o.Append($"    let iban: Option<String> = Some(" + Read(n.IbanField, n.IbanOption) + ".chars().filter(|c| *c != ' ' && *c != '-').collect::<String>().to_uppercase()).filter(|v| !v.is_empty());\n");
            o.Append(Write(n.IbanField, n.IbanOption, "iban") + "\n");
            o.Append($"    if iban.as_deref().is_some_and(|v| !iban_valid(v)) {{\n        return Err({Invalid(PaymentWords.IbanBad)});\n    }}\n");
        }
        else
            o.Append("    let iban: Option<String> = None;\n");
        if (n.AccountField is not null && n.RoutingField is not null)
        {
            o.Append("    let account: Option<String> = Some(" + Read(n.AccountField, n.AccountOption) + ".trim().to_string()).filter(|v| !v.is_empty());\n");
            o.Append("    let routing: Option<String> = Some(" + Read(n.RoutingField, n.RoutingOption) + ".trim().to_string()).filter(|v| !v.is_empty());\n");
            o.Append(Write(n.AccountField, n.AccountOption, "account") + "\n" + Write(n.RoutingField, n.RoutingOption, "routing") + "\n");
            o.Append($"    if routing.as_deref().is_some_and(|v| !routing_valid(v)) {{\n        return Err({Invalid(PaymentWords.RoutingBad)});\n    }}\n");
            o.Append($"    if let (Some(account), Some(_)) = (account.as_deref(), routing.as_deref()) {{\n        if !(4..=17).contains(&account.len()) || !account.chars().all(|c| c.is_ascii_digit()) {{\n            return Err({Invalid(PaymentWords.AccountNumberBad)});\n        }}\n    }}\n");
            o.Append($"    if iban.is_none() && (account.is_none() || routing.is_none()) {{\n        return Err({Invalid(PaymentWords.AccountMissing)});\n    }}\n");
        }
        else
            o.Append($"    if iban.is_none() {{\n        return Err({Invalid(PaymentWords.AccountMissing)});\n    }}\n");
        o.Append("    Ok(())\n}\n");
        return o.ToString();
    }

    private static string InstallmentCode(Names n)
    {
        string zero = RustRateWriter.ZeroOf(n.AmountType!);
        string over = n.IsFloat ? "scheduled - total > 0.000001" : "scheduled > total";
        string toFloat = "format!(\"{}\", §V).parse::<f64>().unwrap_or(0.0)";
        return TemplateText.FillAll(InstallmentText, ("§STRUCT", n.Struct), ("§KT", n.KeyType), ("§DOCMODULE", n.DocModule!), ("§DOCKT", n.DocKeyType!), ("§DOC", n.DocField!), ("§SEQ", n.SeqField!), ("§DUE", n.DueField!), ("§AMT", n.AmountField!),
            ("§TOTAL", n.DocTotalField!), ("§ZERO", zero), ("§OVER", over), ("§TOF_TOTAL", toFloat.Replace("§V", "total")), ("§TOF_SCHEDULED", toFloat.Replace("§V", "scheduled")),
            ("§SELECT", RustTable.Raw($"SELECT {n.SelectList} FROM {n.Table} WHERE {n.DocColumn} = ")), ("§ANDKEYNE", RustTable.Raw($" AND {n.KeyColumn} <> ")),
            ("§INVALIDTYPE", PaymentWords.InvalidType), ("§INVALIDTITLE", PaymentWords.InvalidTitle), ("§OVERTYPE", PaymentWords.OverTotalType), ("§OVERTITLE", PaymentWords.OverTotalTitle),
            ("§W_DOCUMENT", PaymentWords.DocumentMissing), ("§W_SEQ", PaymentWords.SequenceNotPositive), ("§W_AMOUNT", PaymentWords.AmountNotPositive), ("§W_TAKEN", PaymentWords.SequenceTaken), ("§W_ORDER", PaymentWords.OutOfOrder),
            ("§W_OVER", PaymentWords.OverTotal), ("§W_NEEDS", PaymentWords.NeedsDocument));
    }

    private static string LedgerCode(Names n)
    {
        var l = n.Ledger!;
        string stamp = l.StampField is null ? "" : l.StampOption
            ? $"    if row.{l.StampField}.is_none() {{\n        row.{l.StampField} = Some(chrono::Local::now().naive_local());\n    }}\n"
            : $"    if row.{l.StampField} == chrono::NaiveDateTime::default() {{\n        row.{l.StampField} = chrono::Local::now().naive_local();\n    }}\n";
        return TemplateText.FillAll(LedgerText, ("§STRUCT", n.Struct), ("§KT", n.KeyType), ("§ROWKEY", l.RowKey), ("§DOCMODULE", n.DocModule!), ("§DOCKT", n.DocKeyType!), ("§DOC", n.DocField!), ("§KIND", l.KindField), ("§STATUS", l.StatusField),
            ("§AMT", l.AmountField), ("§GATEWAY", l.GatewayField), ("§IDEM", l.KeyField), ("§TOTAL", n.DocTotalField!), ("§FACTOR", Math.Pow(10, l.Scale).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".0"), ("§STAMP", stamp),
            ("§COUNTGATEWAY", RustTable.Raw($"SELECT COUNT(*) FROM {n.Table} WHERE {l.GatewayColumn} = ")), ("§SELECTBYKEY", RustTable.Raw($"SELECT {n.SelectList} FROM {n.Table} WHERE {l.KeyColumn} = ")),
            ("§SELECTHELD", RustTable.Raw($"SELECT {n.SelectList} FROM {n.Table} WHERE {n.DocColumn} = ")), ("§ANDSUCCEEDED", RustTable.Raw($" AND {l.StatusColumn} = '{LedgerPlan.Succeeded}'")),
            ("§ANDPAID", RustTable.Raw($" AND {l.StatusColumn} = '{LedgerPlan.Succeeded}' AND {l.KindColumn} IN ('{LedgerPlan.Capture}', '{LedgerPlan.Refund}')")),
            ("§INVALIDTYPE", PaymentWords.InvalidType), ("§INVALIDTITLE", PaymentWords.InvalidTitle), ("§DUPTYPE", PaymentWords.LedgerDuplicateType), ("§DUPTITLE", PaymentWords.LedgerDuplicateTitle), ("§REFUNDTYPE", PaymentWords.RefundOverType), ("§REFUNDTITLE", PaymentWords.RefundOverTitle),
            ("§W_KIND", PaymentWords.KindBad), ("§W_STATUS", PaymentWords.StatusBad), ("§W_DOCUMENT", PaymentWords.DocumentMissing), ("§W_SIGN", PaymentWords.RefundSign), ("§W_POSITIVE", PaymentWords.AmountPositive), ("§W_GATEWAYMISSING", PaymentWords.GatewayMissing),
            ("§W_GATEWAYTAKEN", PaymentWords.GatewayTaken), ("§W_KEYREUSED", PaymentWords.KeyReused), ("§W_REFUNDOVER", PaymentWords.RefundOver), ("§CAPTURETYPE", PaymentWords.CaptureOverType), ("§CAPTURETITLE", PaymentWords.CaptureOverTitle), ("§W_CAPTUREOVER", PaymentWords.CaptureOver), ("§W_NEEDS", PaymentWords.NeedsDocument));
    }

    private static string DueCode(Names n)
    {
        bool dateTime = n.DueType == "NaiveDateTime" || n.DueType == "chrono::NaiveDateTime";
        bool documentDateTime = n.DocDateType!.EndsWith("NaiveDateTime");
        string start = documentDateTime ? "document_date.date()" : "document_date";
        string dateRead = n.DocDateOption is null ? $"let document_date = document.{n.DocDateField}.clone();" : $"let Some(document_date) = document.{n.DocDateField}.clone() else {{\n        return Ok(());\n    }};";
        string given = dateTime ? "given.date()" : "given";
        string made = dateTime ? "day.and_hms_opt(0, 0, 0).unwrap_or_default()" : "day";
        string termsKey = n.TermsOption ? $"let Some(terms_key) = row.{n.TermsField}.clone() else {{\n            return Ok(());\n        }};" : $"let terms_key = row.{n.TermsField}.clone();";
        return TemplateText.FillAll(DueText, ("§STRUCT", n.Struct), ("§DOCMODULE", n.DocModule!), ("§TERMSMODULE", n.TermsModule!), ("§DOC", n.DocField!), ("§DUE", n.DueField!), ("§DAYS", n.DaysField!), ("§START", start), ("§DATEREAD", dateRead), ("§GIVEN", given), ("§MADE", made),
            ("§TERMSKEY", termsKey), ("§INVALIDTYPE", PaymentWords.InvalidType), ("§INVALIDTITLE", PaymentWords.InvalidTitle), ("§W_DOCUMENT", PaymentWords.DocumentMissing), ("§W_TERMS", PaymentWords.TermsMissing), ("§W_BEFORE", PaymentWords.DueBeforeDocument));
    }

    private static string OptionalNumber(string expression, bool option) => option ? $"{expression}.clone().map(|v| to_number(format!(\"{{}}\", v)))" : $"Some(to_number(format!(\"{{}}\", {expression}.clone())))";

    private static string PayableCode(Names n)
    {
        bool documentDateTime = n.DocDateType!.EndsWith("NaiveDateTime");
        bool dueDateTime = n.DueType!.EndsWith("NaiveDateTime");
        string documentDate = n.DocDateOption is null ? $"Some(document.{n.DocDateField}.clone())" : $"document.{n.DocDateField}.clone()";
        string startDay = documentDateTime ? "start.date()" : "start";
        string dueDay = dueDateTime ? "due.date()" : "due";
        string termsRead = n.TermsOption
            ? $"match row.{n.TermsField}.clone() {{\n        Some(key) => crate::repos::{n.TermsModule}::get_by_id(&pool, key).await?,\n        None => None,\n    }}"
            : $"crate::repos::{n.TermsModule}::get_by_id(&pool, row.{n.TermsField}.clone()).await?";
        var o = new System.Text.StringBuilder();
        o.Append("    if let Some(terms) = &terms {\n");
        if (n.EarlyPercent is not null && n.EarlyDays is not null)
        {
            string days = n.EarlyDaysOption ? $"terms.{n.EarlyDays}.map(|v| v as i64)" : $"Some(terms.{n.EarlyDays} as i64)";
            o.Append($"        if let (Some(percent), Some(days), Some(start)) = ({OptionalNumber("terms." + n.EarlyPercent, n.EarlyPercentOption)}, {days}, {documentDate}) {{\n");
            o.Append($"            if day <= {startDay} + chrono::Duration::days(days) {{\n                discount = round(total * percent / 100.0);\n                rule = \"{PaymentWords.RuleEarly}\";\n            }}\n        }}\n");
        }
        if (n.LatePercent is not null)
        {
            o.Append($"        if rule == \"{PaymentWords.RuleOnTime}\" {{\n");
            o.Append($"            if let (Some(percent), Some(due)) = ({OptionalNumber("terms." + n.LatePercent, n.LatePercentOption)}, row.{n.DueField}.clone()) {{\n");
            o.Append($"                if day > {dueDay} {{\n                    penalty = round(total * percent / 100.0);\n                    rule = \"{PaymentWords.RuleLate}\";\n                }}\n            }}\n        }}\n");
        }
        o.Append("    }\n");
        return TemplateText.FillAll(PayableText, ("§STRUCT", n.Struct), ("§KT", n.KeyType), ("§DOCMODULE", n.DocModule!), ("§DOCKT", n.DocKeyType!), ("§DOC", n.DocField!), ("§DUE", n.DueField!), ("§TERMSREAD", termsRead), ("§TOTAL", n.TotalField!),
            ("§FACTOR", Math.Pow(10, n.Scale).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".0"), ("§BODY", o.ToString()), ("§RULEONTIME", PaymentWords.RuleOnTime),
            ("§DUEJSON", dueDateTime ? "row." + n.DueField + ".map(|d| d.date().to_string())" : "row." + n.DueField + ".map(|d| d.to_string())"),
            ("§INVALIDTYPE", PaymentWords.InvalidType), ("§INVALIDTITLE", PaymentWords.InvalidTitle), ("§W_NEEDSPAY", PaymentWords.NeedsPayment), ("§W_PAYMISSING", PaymentWords.PaymentMissing), ("§W_DOCUMENT", PaymentWords.DocumentMissing));
    }

    private const string PayableText = """"
#[derive(serde::Deserialize)]
struct PayableQuery {
    id: Option<§KT>,
    on: Option<chrono::NaiveDate>,
}

fn to_number(text: String) -> f64 {
    text.parse::<f64>().unwrap_or(0.0)
}

fn round(value: f64) -> f64 {
    (value * §FACTOR).round() / §FACTOR
}

/// What is payable on a day (today without one): the total less the early-payment discount while the discount days of the terms last, plus the late penalty once the due day has passed.
async fn payable_on(State(pool): State<Pool>, Query(query): Query<PayableQuery>) -> ApiResult<Json<serde_json::Value>> {
    let Some(id) = query.id else {
        return Err(ApiError::BadRequest("§W_NEEDSPAY".into()));
    };
    let Some(row) = repo::get_by_id(&pool, id.clone()).await? else {
        return Err(ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", "§W_PAYMISSING".into()));
    };
    let Some(document) = crate::repos::§DOCMODULE::get_by_id(&pool, row.§DOC.clone()).await? else {
        return Err(ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", "§W_DOCUMENT".into()));
    };
    let terms = §TERMSREAD;
    let total = to_number(format!("{}", document.§TOTAL));
    let day = query.on.unwrap_or_else(|| chrono::Local::now().date_naive());
    let mut discount = 0.0_f64;
    let mut penalty = 0.0_f64;
    let mut rule = "§RULEONTIME";
§BODY    Ok(Json(serde_json::json!({
        "id": id,
        "document": row.§DOC,
        "total": total,
        "day": day.to_string(),
        "dueDate": §DUEJSON,
        "discount": discount,
        "penalty": penalty,
        "payable": round(total - discount + penalty),
        "rule": rule,
    })))
}

"""";

    private const string AccountText = """"
const IBAN_LENGTHS: &str = "§LENGTHS";

/// An IBAN has the length of its country, and the number of its text with the first four characters moved to the end and the letters written as 10 to 35 leaves 1 when divided by 97.
fn iban_valid(iban: &str) -> bool {
    if iban.len() < 5 || !iban.is_ascii() || !iban.chars().all(|c| c.is_ascii_alphanumeric()) {
        return false;
    }
    let bytes = iban.as_bytes();
    if !bytes[0].is_ascii_alphabetic() || !bytes[1].is_ascii_alphabetic() || !bytes[2].is_ascii_digit() || !bytes[3].is_ascii_digit() {
        return false;
    }
    let length = IBAN_LENGTHS.split(' ').find(|code| code.starts_with(&iban[..2])).and_then(|code| code[2..].parse::<usize>().ok());
    if length != Some(iban.len()) {
        return false;
    }
    let mut remainder: u32 = 0;
    for c in iban[4..].chars().chain(iban[..4].chars()) {
        let value = c.to_digit(36).unwrap_or(0);
        remainder = if value >= 10 { (remainder * 100 + value) % 97 } else { (remainder * 10 + value) % 97 };
    }
    remainder == 1
}

/// A routing number has nine digits and 3 * (1st + 4th + 7th) + 7 * (2nd + 5th + 8th) + (3rd + 6th + 9th) is a multiple of ten.
fn routing_valid(routing: &str) -> bool {
    if routing.len() != 9 || !routing.chars().all(|c| c.is_ascii_digit()) {
        return false;
    }
    let d: Vec<u32> = routing.chars().filter_map(|c| c.to_digit(10)).collect();
    (3 * (d[0] + d[3] + d[6]) + 7 * (d[1] + d[4] + d[7]) + (d[2] + d[5] + d[8])) % 10 == 0
}

/// The IBAN is written without spaces and in upper case and must be valid; an account number with a routing number must be valid too; one of the two is needed.
fn check_account(row: &mut §STRUCT) -> ApiResult<()> {
"""";

    private const string InstallmentText = """"
/// The rows of the other installments of the document.
async fn other_installments(pool: &Pool, row_id: Option<§KT>, document: §DOCKT) -> ApiResult<Vec<§STRUCT>> {
    let mut builder = sqlx::QueryBuilder::<Db>::new(§SELECT);
    builder.push_bind(document);
    if let Some(id) = row_id {
        builder.push(§ANDKEYNE).push_bind(id);
    }
    Ok(builder.build_query_as::<§STRUCT>().fetch_all(pool).await?)
}

/// The document exists, the sequence number and the amount are above zero, no other installment of the document has the number, the due dates follow the numbers, and the installments add up to no more than the total of the document.
async fn check_installment(pool: &Pool, row_id: Option<§KT>, row: &mut §STRUCT) -> ApiResult<()> {
    let Some(document) = crate::repos::§DOCMODULE::get_by_id(pool, row.§DOC.clone()).await? else {
        return Err(ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", "§W_DOCUMENT".into()));
    };
    if row.§SEQ <= 0 {
        return Err(ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", "§W_SEQ".into()));
    }
    if row.§AMT <= §ZERO {
        return Err(ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", "§W_AMOUNT".into()));
    }
    let others = other_installments(pool, row_id, row.§DOC.clone()).await?;
    if others.iter().any(|other| other.§SEQ == row.§SEQ) {
        return Err(ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", "§W_TAKEN".into()));
    }
    if others.iter().any(|other| (other.§SEQ < row.§SEQ && other.§DUE > row.§DUE) || (other.§SEQ > row.§SEQ && other.§DUE < row.§DUE)) {
        return Err(ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", "§W_ORDER".into()));
    }
    let scheduled = others.iter().fold(§ZERO, |sum, other| sum + other.§AMT.clone()) + row.§AMT.clone();
    let total = document.§TOTAL.clone();
    if §OVER {
        return Err(ApiError::Rejected(400, "§OVERTYPE", "§OVERTITLE", "§W_OVER".into()));
    }
    Ok(())
}

#[derive(serde::Deserialize)]
struct ScheduleQuery {
    document: Option<§DOCKT>,
}

/// What the installments of a document come to: the total, the sum scheduled, what is left to schedule and whether the schedule is complete.
async fn installment_schedule(State(pool): State<Pool>, Query(query): Query<ScheduleQuery>) -> ApiResult<Json<serde_json::Value>> {
    let Some(id) = query.document else {
        return Err(ApiError::BadRequest("§W_NEEDS".into()));
    };
    let Some(document) = crate::repos::§DOCMODULE::get_by_id(&pool, id.clone()).await? else {
        return Err(ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", "§W_DOCUMENT".into()));
    };
    let rows = other_installments(&pool, None, id.clone()).await?;
    let total = document.§TOTAL.clone();
    let scheduled = rows.iter().fold(§ZERO, |sum, row| sum + row.§AMT.clone());
    let total_number = §TOF_TOTAL;
    let scheduled_number = §TOF_SCHEDULED;
    Ok(Json(serde_json::json!({
        "document": id,
        "total": total_number,
        "scheduled": scheduled_number,
        "remaining": total_number - scheduled_number,
        "complete": (total_number - scheduled_number).abs() < 0.000001,
        "installments": rows.len(),
    })))
}

"""";

    private const string DueText = """"
/// A row with payment terms and no due date gets the date of the document plus the days to pay; a due date before the date of the document is refused.
async fn check_due(pool: &Pool, row: &mut §STRUCT) -> ApiResult<()> {
    let Some(document) = crate::repos::§DOCMODULE::get_by_id(pool, row.§DOC.clone()).await? else {
        return Err(ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", "§W_DOCUMENT".into()));
    };
    §DATEREAD
    let start = §START;
    match row.§DUE.clone() {
        None => {
            §TERMSKEY
            let Some(terms) = crate::repos::§TERMSMODULE::get_by_id(pool, terms_key).await? else {
                return Err(ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", "§W_TERMS".into()));
            };
            let day = start + chrono::Duration::days(terms.§DAYS as i64);
            row.§DUE = Some(§MADE);
            Ok(())
        }
        Some(given) => {
            if §GIVEN < start {
                return Err(ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", "§W_BEFORE".into()));
            }
            Ok(())
        }
    }
}

"""";

    private const string LedgerText = """"
fn ledger_invalid(detail: &str) -> ApiError {
    ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", detail.into())
}

fn ledger_number(value: impl std::fmt::Display) -> f64 {
    format!("{}", value).parse::<f64>().unwrap_or(0.0)
}

fn ledger_round(value: f64) -> f64 {
    (value * §FACTOR).round() / §FACTOR
}

/// The kind and status are one of the ledger's words (written in lower case), the document exists, a refund is a negative amount and any other kind a positive one, the gateway id and the idempotency key are given and a gateway id is not recorded yet, a retry (an idempotency key recorded for the same document, kind and amount) answers with the stored row, the key of a different transaction is refused, a succeeded refund is no more than what has been paid so far and a succeeded capture does not take what is paid above the total of the document.
async fn check_ledger(pool: &Pool, row: &mut §STRUCT) -> ApiResult<Option<§STRUCT>> {
    row.§KIND = row.§KIND.trim().to_lowercase();
    row.§STATUS = row.§STATUS.trim().to_lowercase();
    row.§GATEWAY = row.§GATEWAY.trim().to_string();
    row.§IDEM = row.§IDEM.trim().to_string();
    if !["authorize", "capture", "refund", "void"].contains(&row.§KIND.as_str()) {
        return Err(ledger_invalid("§W_KIND"));
    }
    if !["pending", "succeeded", "failed"].contains(&row.§STATUS.as_str()) {
        return Err(ledger_invalid("§W_STATUS"));
    }
    if crate::repos::§DOCMODULE::get_by_id(pool, row.§DOC.clone()).await?.is_none() {
        return Err(ledger_invalid("§W_DOCUMENT"));
    }
    let amount = ledger_number(row.§AMT.clone());
    let refund = row.§KIND == "refund";
    if (refund && amount >= 0.0) || (!refund && amount <= 0.0) {
        return Err(ledger_invalid(if refund { "§W_SIGN" } else { "§W_POSITIVE" }));
    }
    if row.§GATEWAY.is_empty() || row.§IDEM.is_empty() {
        return Err(ledger_invalid("§W_GATEWAYMISSING"));
    }
    let mut key = sqlx::QueryBuilder::<Db>::new(§SELECTBYKEY);
    key.push_bind(row.§IDEM.clone());
    if let Some(again) = key.build_query_as::<§STRUCT>().fetch_optional(pool).await? {
        if again.§DOC == row.§DOC && again.§KIND == row.§KIND && (ledger_number(again.§AMT.clone()) - amount).abs() < 0.000001 {
            return Ok(Some(again));
        }
        return Err(ApiError::Rejected(409, "§DUPTYPE", "§DUPTITLE", "§W_KEYREUSED".into()));
    }
    let mut gateway = sqlx::QueryBuilder::<Db>::new(§COUNTGATEWAY);
    gateway.push_bind(row.§GATEWAY.clone());
    if gateway.build_query_scalar::<i64>().fetch_one(pool).await? > 0 {
        return Err(ApiError::Rejected(409, "§DUPTYPE", "§DUPTITLE", "§W_GATEWAYTAKEN".into()));
    }
    if row.§STATUS == "succeeded" && (refund || row.§KIND == "capture") {
        let mut held = sqlx::QueryBuilder::<Db>::new(§SELECTHELD);
        held.push_bind(row.§DOC.clone());
        held.push(§ANDPAID);
        let rows = held.build_query_as::<§STRUCT>().fetch_all(pool).await?;
        let paid: f64 = rows.iter().map(|other| ledger_number(other.§AMT.clone())).sum::<f64>() + amount;
        if paid < -0.000001 {
            return Err(ApiError::Rejected(400, "§REFUNDTYPE", "§REFUNDTITLE", "§W_REFUNDOVER".into()));
        }
        let limit = match crate::repos::§DOCMODULE::get_by_id(pool, row.§DOC.clone()).await? {
            Some(document) => ledger_number(document.§TOTAL.clone()),
            None => 0.0,
        };
        if paid > limit + 0.000001 {
            return Err(ApiError::Rejected(400, "§CAPTURETYPE", "§CAPTURETITLE", "§W_CAPTUREOVER".into()));
        }
    }
    Ok(None)
}

fn existing_response(row: §STRUCT) -> Response {
    Json(row).into_response()
}

/// Checks a row and stores it; the answer is the stored row (a retry of a recorded transaction answers with the row stored the first time, 200).
async fn record_transaction(State(pool): State<Pool>, ApiJson(mut row): ApiJson<§STRUCT>) -> ApiResult<Response> {
    row.§ROWKEY = Default::default();
§STAMP    if let Some(existing) = check_ledger(&pool, &mut row).await? {
        return Ok(existing_response(existing));
    }
    let row = repo::insert(&pool, &row).await?;
    Ok(created(format!("record/{}", row.§ROWKEY), row))
}

#[derive(serde::Deserialize)]
struct PaidQuery {
    document: Option<§DOCKT>,
}

/// What a document has been paid, worked out from the succeeded rows: the captured amount, the refunded amount, what is paid (captured less refunded), what is left of the total and whether the total is paid.
async fn paid_amount(State(pool): State<Pool>, Query(query): Query<PaidQuery>) -> ApiResult<Json<serde_json::Value>> {
    let Some(id) = query.document else {
        return Err(ApiError::BadRequest("§W_NEEDS".into()));
    };
    let Some(document) = crate::repos::§DOCMODULE::get_by_id(&pool, id.clone()).await? else {
        return Err(ledger_invalid("§W_DOCUMENT"));
    };
    let mut held = sqlx::QueryBuilder::<Db>::new(§SELECTHELD);
    held.push_bind(id.clone());
    held.push(§ANDSUCCEEDED);
    let rows = held.build_query_as::<§STRUCT>().fetch_all(&pool).await?;
    let total = ledger_number(document.§TOTAL.clone());
    let captured: f64 = rows.iter().filter(|r| r.§KIND == "capture").map(|r| ledger_number(r.§AMT.clone())).sum();
    let refunded: f64 = -rows.iter().filter(|r| r.§KIND == "refund").map(|r| ledger_number(r.§AMT.clone())).sum::<f64>();
    let paid = captured - refunded;
    Ok(Json(serde_json::json!({
        "document": id,
        "total": ledger_round(total),
        "captured": ledger_round(captured),
        "refunded": ledger_round(refunded),
        "paid": ledger_round(paid),
        "remaining": ledger_round(total - paid),
        "settled": paid >= total - 0.000001,
        "transactions": rows.len(),
    })))
}

"""";
}
