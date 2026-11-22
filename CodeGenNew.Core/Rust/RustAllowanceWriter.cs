using CodeGenNew.Core.Sql;
using CodeGenNew.Core.Allowance;

namespace CodeGenNew.Core.Rust;

/// <summary> The Rust (axum, sqlx) counterpart of <see cref="AllowanceChargeWriter"/> (<c>AllowanceCharges</c>): the table's router gets <c>check_allowance</c>, called before the row is written, and a
/// <c>GET allowance-charge-totals</c> route when the entry names the document column. The words are <see cref="AllowanceChargeWords"/>. With a <c>Decimal</c> column the work is done in <c>Decimal</c> (half away
/// from zero), otherwise in <c>f64</c> (SQLite has no <c>rust_decimal</c>). </summary>
public static class RustAllowanceWriter
{
    /// <summary> The table as Rust fields and base types (<c>Decimal</c>, <c>f64</c> or <c>f32</c>). <c>Document</c> is the document field and <c>DocumentColumn</c>, <c>Table</c> and <c>SelectList</c> what the totals query needs. </summary>
    public sealed record Names(string Struct, string Table, string SelectList, string Flag, string Base, string BaseType, bool BaseNullable, string Percent, string PercentType, bool PercentNullable,
        string Amount, string AmountType, int AmountScale, int PercentScale, string? Document, string? DocumentColumn, string? DocumentType, bool DocumentNullable = false, string? LineField = null, string? LineColumn = null, string? LineType = null, bool LineNullable = false);

    /// <summary> The names of an allowance table, or null without one. </summary>
    public static Names? NamesFor(AllowanceChargeShape? plan, RustTable t, DialectInfo dialect)
    {
        if (plan is null)
            return null;
        RustField F(ColumnModel c) => t.FieldOf(c);
        RustField? document = plan.Document is null ? null : F(plan.Document);
        RustField? line = plan.Line is null ? null : F(plan.Line);
        return new Names(t.Struct, t.Table, t.SelectList, F(plan.Flag).Name, F(plan.Base).Name, F(plan.Base).BaseType, plan.Base.IsNullable, F(plan.Percent).Name, F(plan.Percent).BaseType, plan.Percent.IsNullable,
            F(plan.Amount).Name, F(plan.Amount).BaseType, plan.Amount.Scale ?? AllowanceChargeWords.DefaultScale, plan.Percent.Scale ?? AllowanceChargeWords.DefaultScale, document?.Name,
            plan.Document is null ? null : dialect.Quote(plan.Document.DbName), document?.BaseType, plan.Document?.IsNullable ?? false, line?.Name, plan.Line is null ? null : dialect.Quote(plan.Line.DbName),
            line?.BaseType, plan.Line?.IsNullable ?? false);
    }

    public static string RouteLine(string route, string readLayer) => $"        .route(\"{route}/{AllowanceChargeWords.TotalsRoute}\", get(allowance_charge_totals){readLayer})\n";

    private static bool Decimals(Names n) => new[] { n.BaseType, n.PercentType, n.AmountType }.Contains("Decimal");

    private static string Read(string field, string type, bool nullable, bool decimals)
    {
        if (decimals)
            return nullable ? $"row.{field}.map(|v| {RustRateDocumentWriter.ToDecimal("v", type)}).unwrap_or_default()" : RustRateDocumentWriter.ToDecimal("row." + field, type);
        return nullable ? $"row.{field}.unwrap_or_default() as f64" : $"row.{field} as f64";
    }

    public static string Code(Names n)
    {
        bool decimals = Decimals(n);
        string zero = decimals ? "rust_decimal::Decimal::ZERO" : "0.0";
        string hundred = decimals ? "rust_decimal::Decimal::from(100)" : "100.0";
        string amountValue = decimals
            ? RustRateDocumentWriter.FromDecimal($"(base * percent / {hundred}).round_dp_with_strategy({n.AmountScale}, rust_decimal::RoundingStrategy::MidpointAwayFromZero)", n.AmountType)
            : $"((base * percent / 100.0 * 10f64.powi({n.AmountScale})).round() / 10f64.powi({n.AmountScale})) as {n.AmountType}";
        string percentValue = decimals
            ? RustRateDocumentWriter.FromDecimal($"(amount / base * {hundred}).round_dp_with_strategy({n.PercentScale}, rust_decimal::RoundingStrategy::MidpointAwayFromZero)", n.PercentType)
            : $"((amount / base * 100.0 * 10f64.powi({n.PercentScale})).round() / 10f64.powi({n.PercentScale})) as {n.PercentType}";
        return TemplateText.FillAll(Text, ("§STRUCT", n.Struct), ("§BASEVALUE", Read(n.Base, n.BaseType, n.BaseNullable, decimals)), ("§PCTVALUE", Read(n.Percent, n.PercentType, n.PercentNullable, decimals)),
            ("§AMTVALUE", Read(n.Amount, n.AmountType, false, decimals)), ("§AMT", n.Amount), ("§PCT", n.Percent), ("§ZERO", zero),
            ("§DECIMALUSE", decimals ? "    #[allow(unused_imports)]\n    use rust_decimal::prelude::ToPrimitive as _;\n" : ""),
            ("§AMOUNTWRITE", amountValue), ("§PERCENTWRITE", n.PercentNullable ? $"Some({percentValue})" : percentValue),
            ("§TYPE", AllowanceChargeWords.InvalidType), ("§TITLE", AllowanceChargeWords.InvalidTitle), ("§W_NEGATIVE", AllowanceChargeWords.Negative),
            ("§W_PERCENTBASE", AllowanceChargeWords.PercentNeedsBase), ("§W_AMOUNT", AllowanceChargeWords.NeedsAmount));
    }

    private const string Text = """"
/// The amount is worked out here: from the base and the percentage, or the percentage from the amount and the base. It is stored positive, the flag says whether it is added or taken off.
fn check_allowance(row: &mut §STRUCT) -> ApiResult<()> {
§DECIMALUSE    let base = §BASEVALUE;
    let percent = §PCTVALUE;
    let amount = §AMTVALUE;
    let zero = §ZERO;
    if base < zero || percent < zero || amount < zero {
        return Err(ApiError::Rejected(400, "§TYPE", "§TITLE", "§W_NEGATIVE".to_string()));
    }
    if percent > zero && base <= zero {
        return Err(ApiError::Rejected(400, "§TYPE", "§TITLE", "§W_PERCENTBASE".to_string()));
    }
    if percent > zero {
        row.§AMT = §AMOUNTWRITE;
    } else if base > zero && amount > zero {
        row.§PCT = §PERCENTWRITE;
    } else if amount <= zero {
        return Err(ApiError::Rejected(400, "§TYPE", "§TITLE", "§W_AMOUNT".to_string()));
    }
    Ok(())
}

"""";

    /// <summary> <paramref name="userArgument"/> and <paramref name="filterArguments"/> are set for a table limited to an owner or a tenant: the rows come from the repository's scoped read and the document is picked from them. </summary>
    public static string Totals(Names n, string? userArgument = null, string? filterArguments = null)
    {
        if (n.Document is null)
            return "";
        bool decimals = Decimals(n);
        string sum = decimals ? "rust_decimal::Decimal" : "f64";
        string round(string value) => decimals ? $"({value}).to_f64().unwrap_or_default()" : $"(({value}) * 10f64.powi({n.AmountScale})).round() / 10f64.powi({n.AmountScale})";
        return TemplateText.FillAll(TotalsText, ("§STRUCT", n.Struct), ("§SELECT", RustTable.Raw($"SELECT {n.SelectList} FROM {n.Table} WHERE {n.DocumentColumn} = ")), ("§DOCTYPE", n.DocumentType ?? "i32"), ("§LINEMEMBER", n.LineType is null ? "" : $"    line: Option<{n.LineType}>,\n"), ("§USERARG", userArgument ?? ""),
            ("§FETCH", filterArguments is null ? UnscopedFetch(RustTable.Raw($"SELECT {n.SelectList} FROM {n.Table} WHERE {n.DocumentColumn} = "), n.Struct, n.LineColumn is null ? null : RustTable.Raw($" AND {n.LineColumn} = ")) : $"    let rows = repo::get_all_scoped(&pool{filterArguments}).await?.into_iter().filter(|r| r.{n.Document} == {(n.DocumentNullable ? "Some(document)" : "document")}{(n.LineField is null ? "" : $" && query.line.map_or(true, |l| r.{n.LineField} == {(n.LineNullable ? "Some(l)" : "l")})")}).collect::<Vec<_>>();\n"),
            ("§FLAG", n.Flag), ("§AMTVALUE", Read(n.Amount, n.AmountType, false, decimals).Replace("row.", "r.")), ("§SUMTYPE", sum), ("§ZERO", decimals ? "rust_decimal::Decimal::ZERO" : "0.0"),
            ("§DECIMALUSE", decimals ? "    #[allow(unused_imports)]\n    use rust_decimal::prelude::ToPrimitive as _;\n" : ""),
            ("§OUTALLOW", round("allowances")), ("§OUTCHARGES", round("charges")), ("§OUTNET", round("charges - allowances")), ("§W_DOC", AllowanceChargeWords.NeedsDocument));
    }

    private static string UnscopedFetch(string select, string structName, string? lineClause) =>
        $"    let mut builder = sqlx::QueryBuilder::<Db>::new({select});\n    builder.push_bind(document);\n"
        + (lineClause is null ? "" : $"    if let Some(line) = query.line {{\n        builder.push({lineClause}).push_bind(line);\n    }}\n")
        + $"    let rows = builder.build_query_as::<{structName}>().fetch_all(&pool).await?;\n";

    private const string TotalsText = """"
#[derive(serde::Deserialize)]
struct AllowanceChargeTotalsQuery {
    document: Option<§DOCTYPE>,
§LINEMEMBER}

/// The allowances, the charges and the net (charges minus allowances) of one document.
async fn allowance_charge_totals(State(pool): State<Pool>, Query(query): Query<AllowanceChargeTotalsQuery>§USERARG) -> ApiResult<Json<serde_json::Value>> {
§DECIMALUSE    let Some(document) = query.document else {
        return Err(ApiError::BadRequest("§W_DOC".into()));
    };
§FETCH    let mut allowances: §SUMTYPE = §ZERO;
    let mut charges: §SUMTYPE = §ZERO;
    for r in &rows {
        let amount = §AMTVALUE;
        if r.§FLAG {
            charges += amount;
        } else {
            allowances += amount;
        }
    }
    Ok(Json(serde_json::json!({
        "allowances": §OUTALLOW,
        "charges": §OUTCHARGES,
        "net": §OUTNET,
        "rows": rows.len()
    })))
}

"""";
}
