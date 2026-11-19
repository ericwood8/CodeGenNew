using CodeGenNew.Core.Tax;





namespace CodeGenNew.Core.Rust;

/// <summary> The Rust (axum, sqlx) counterpart of <see cref="TaxLineWriter"/> (<c>TaxLines</c>): the line's router gets <c>apply_tax</c>, called before the row is written, and a <c>GET tax-subtotals</c> route when the
/// entry names the document column. The words are <see cref="TaxWords"/>. </summary>
public static class RustTaxWriter
{
    /// <summary> The line and its category table as Rust fields and base types (<c>Decimal</c>, <c>f64</c>, <c>f32</c> or a whole number; days are <c>NaiveDate</c> or <c>NaiveDateTime</c>).
    /// <c>Label</c> is the category's label field, <c>Document</c> the line's document field and <c>DocumentColumn</c>, <c>Table</c> and <c>SelectList</c> what the subtotals query needs. </summary>
    public sealed record Names(string Struct, string Table, string SelectList, string CategoryModule, string Category, string CategoryType, bool CategoryNullable, string Rate, string RateType, string Taxable, string TaxableType,
        string Tax, string TaxType, int Scale, string CategoryRate, string CategoryRateType, string? ValidFrom, string? ValidFromType, string? ValidTo, bool ValidToNullable, string? Day, string? DayType, string? Document,
        string? DocumentColumn, string? DocumentType, string? Label);

    public static string RouteLine(string route, string readLayer) => $"        .route(\"{route}/tax-subtotals\", get(tax_subtotals){readLayer})\n";

    private static string Zero(string type) => type switch { "Decimal" => "rust_decimal::Decimal::ZERO", "f32" or "f64" => "0.0", _ => "0" };

    public static string Code(Names n)
    {
        bool decimals = new[] { n.TaxableType, n.RateType, n.TaxType, n.CategoryRateType }.Contains("Decimal");
        string foundRate = n.CategoryRateType == n.RateType ? $"found.{n.CategoryRate}" : decimals ? RustRateDocumentWriter.FromDecimal(RustRateDocumentWriter.ToDecimal($"found.{n.CategoryRate}", n.CategoryRateType), n.RateType) : $"found.{n.CategoryRate} as {n.RateType}";
        string taxValue = decimals
            ? RustRateDocumentWriter.FromDecimal($"({RustRateDocumentWriter.ToDecimal("row." + n.Taxable, n.TaxableType)} * {RustRateDocumentWriter.ToDecimal("row." + n.Rate, n.RateType)}).round_dp_with_strategy({n.Scale}, rust_decimal::RoundingStrategy::MidpointAwayFromZero)", n.TaxType)
            : $"((row.{n.Taxable} as f64 * row.{n.Rate} as f64 * 10f64.powi({n.Scale})).round() / 10f64.powi({n.Scale})) as {n.TaxType}";
        string none = n.CategoryNullable
            ? $"    let Some(category) = row.{n.Category}.clone() else {{\n        row.{n.Rate} = {Zero(n.RateType)};\n        row.{n.Tax} = {Zero(n.TaxType)};\n        return Ok(());\n    }};\n"
            : $"    let category = row.{n.Category}.clone();\n";
        bool dating = n.ValidFrom is not null;
        string catDayType = n.ValidFromType ?? "";
        string today = catDayType == "NaiveDateTime" ? "chrono::Local::now().date_naive().and_hms_opt(0, 0, 0).unwrap_or_default()" : "chrono::Local::now().date_naive()";
        string asOf = n.Day is null ? today
            : n.DayType == catDayType || !dating ? $"row.{n.Day}"
            : catDayType == "NaiveDate" ? $"row.{n.Day}.date()" : $"row.{n.Day}.and_hms_opt(0, 0, 0).unwrap_or_default()";
        string day = !dating ? "" : $"    let as_of = {asOf};\n";
        string sameDay = n.Day is null ? "" : n.DayType == "NaiveDateTime" ? $" && stored.{n.Day}.date() == row.{n.Day}.date()" : $" && stored.{n.Day} == row.{n.Day}";
        string dated = "";
        if (dating)
        {
            string ended = n.ValidTo is null ? "" : n.ValidToNullable ? $" || found.{n.ValidTo}.map_or(false, |end| end < as_of)" : $" || found.{n.ValidTo} < as_of";
            dated = $"            if found.{n.ValidFrom} > as_of{ended} {{\n                return Err(ApiError::Rejected(400, \"{TaxWords.NotInForceType}\", \"{TaxWords.NotInForceTitle}\", format!(\"Tax category {{}} is not in force on {{}}.\", category, as_of.format(\"%Y-%m-%d\"))));\n            }}\n";
        }
        return TemplateText.FillAll(Text, ("§STRUCT", n.Struct), ("§NONE", none), ("§DAY", day), ("§CAT", n.Category), ("§RATE", n.Rate), ("§TAX", n.Tax), ("§SAMEDAY", sameDay), ("§CATMODULE", n.CategoryModule), ("§DATED", dated),
            ("§FOUNDRATE", foundRate), ("§TAXVALUE", taxValue), ("§DECIMALUSE", decimals ? "    #[allow(unused_imports)]\n    use rust_decimal::prelude::ToPrimitive as _;\n" : ""),
            ("§UNKNOWNTYPE", TaxWords.UnknownType), ("§UNKNOWNTITLE", TaxWords.UnknownTitle), ("§W_UNKNOWN", "There is no tax category {}."));
    }

    private const string Text = """"
/// The line carries the rate of its category: copied when the row is created or its category (or day) changes, kept otherwise. The tax is the taxable amount times the rate over 100.
async fn apply_tax(pool: &Pool, row: &mut §STRUCT, stored: Option<&§STRUCT>) -> ApiResult<()> {
§DECIMALUSE§NONE§DAY    match stored {
        Some(stored) if stored.§CAT == row.§CAT§SAMEDAY => row.§RATE = stored.§RATE.clone(),
        _ => {
            let Some(found) = crate::repos::§CATMODULE::get_by_id(pool, category.clone()).await? else {
                return Err(ApiError::Rejected(400, "§UNKNOWNTYPE", "§UNKNOWNTITLE", format!("§W_UNKNOWN", category)));
            };
§DATED            row.§RATE = §FOUNDRATE;
        }
    }
    row.§TAX = §TAXVALUE;
    Ok(())
}

"""";

    public static string Subtotals(Names n)
    {
        if (n.Document is null)
            return "";
        bool decimals = new[] { n.TaxableType, n.RateType, n.TaxType }.Contains("Decimal");
        string toDec(string value, string type) => RustRateDocumentWriter.ToDecimal(value, type);
        string some = n.CategoryNullable ? $"r.{n.Category}.clone()" : $"Some(r.{n.Category}.clone())";
        string label = n.Label is null ? "serde_json::Value::Null" : $"match &category {{\n            Some(id) => crate::repos::{n.CategoryModule}::get_by_id(&pool, id.clone()).await?.map_or(serde_json::Value::Null, |c| serde_json::json!(c.{n.Label})),\n            None => serde_json::Value::Null,\n        }}";
        return TemplateText.FillAll(SubtotalsText, ("§STRUCT", n.Struct), ("§SELECT", RustTable.Raw($"SELECT {n.SelectList} FROM {n.Table} WHERE {n.DocumentColumn} = ")), ("§DOCTYPE", n.DocumentType ?? "i32"),
            ("§CATTYPE", n.CategoryType), ("§RATETYPE", n.RateType == "Decimal" ? "rust_decimal::Decimal" : n.RateType), ("§SOME", some), ("§CAT", n.Category), ("§RATE", n.Rate), ("§TAXABLE", decimals ? toDec("r." + n.Taxable, n.TaxableType) : $"r.{n.Taxable} as f64"), ("§TAXVALUE", decimals ? toDec("r." + n.Tax, n.TaxType) : $"r.{n.Tax} as f64"),
            ("§SUMTYPE", decimals ? "rust_decimal::Decimal" : "f64"), ("§OUTTAXABLE", decimals ? "taxable.to_f64().unwrap_or_default()" : $"(taxable * 10f64.powi({n.Scale})).round() / 10f64.powi({n.Scale})"),
            ("§OUTTAX", decimals ? "tax.to_f64().unwrap_or_default()" : $"(tax * 10f64.powi({n.Scale})).round() / 10f64.powi({n.Scale})"),
            ("§RATEJSON", decimals && n.RateType != "f64" ? RustRateDocumentWriter.FromDecimal(toDec("rate", n.RateType), "f64") : n.RateType == "f64" ? "rate" : "rate as f64"), ("§LABEL", label), ("§DECIMALUSE", decimals ? "    #[allow(unused_imports)]\n    use rust_decimal::prelude::ToPrimitive as _;\n" : ""),
            ("§W_DOC", TaxWords.SubtotalsNeedsDocument));
    }

    private const string SubtotalsText = """"
#[derive(serde::Deserialize)]
struct TaxSubtotalsQuery {
    document: Option<§DOCTYPE>,
}

/// The tax of one document by category and rate: taxable amount, tax and number of lines, worked out from its lines.
async fn tax_subtotals(State(pool): State<Pool>, Query(query): Query<TaxSubtotalsQuery>) -> ApiResult<Json<serde_json::Value>> {
§DECIMALUSE    let Some(document) = query.document else {
        return Err(ApiError::BadRequest("§W_DOC".into()));
    };
    let mut builder = sqlx::QueryBuilder::<Db>::new(§SELECT);
    builder.push_bind(document);
    let rows = builder.build_query_as::<§STRUCT>().fetch_all(&pool).await?;
    let mut groups: Vec<(Option<§CATTYPE>, §RATETYPE, §SUMTYPE, §SUMTYPE, i64)> = Vec::new();
    for r in &rows {
        let category = §SOME;
        let taxable = §TAXABLE;
        let tax = §TAXVALUE;
        match groups.iter_mut().find(|g| g.0 == category && g.1 == r.§RATE) {
            Some(group) => {
                group.2 += taxable;
                group.3 += tax;
                group.4 += 1;
            }
            None => groups.push((category, r.§RATE.clone(), taxable, tax, 1)),
        }
    }
    groups.sort_by(|a, b| a.0.cmp(&b.0).then(a.1.partial_cmp(&b.1).unwrap_or(std::cmp::Ordering::Equal)));
    let mut answer = Vec::new();
    for (category, rate, taxable, tax, lines) in groups {
        let label = §LABEL;
        answer.push(serde_json::json!({ "category": category, "label": label, "rate": §RATEJSON, "taxable": §OUTTAXABLE, "tax": §OUTTAX, "lines": lines }));
    }
    Ok(Json(serde_json::Value::Array(answer)))
}

"""";
}
