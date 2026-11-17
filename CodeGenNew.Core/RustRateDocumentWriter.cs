namespace CodeGenNew.Core;

/// <summary> The Rust (axum, sqlx) counterpart of the rate document part of <see cref="RowShapeWriter"/> (<c>RateDocuments</c>): the document's router gets <c>convert_currency</c>, called before the row is written,
/// which writes the currency trimmed and in upper case, the rate in force on the day (kept while the currency and day stay) and the converted amount. The words are <see cref="RateWords"/>. </summary>
public static class RustRateDocumentWriter
{
    /// <summary> The document as Rust fields and base types (<c>Decimal</c>, <c>f64</c>, <c>f32</c> or a whole number). <c>DayType</c> is <c>NaiveDate</c> or <c>NaiveDateTime</c> (null: the rate is looked up for today);
    /// <c>RateDayType</c> and <c>FoundRateType</c> are those of the rate table. </summary>
    public sealed record Names(string Struct, string Currency, string Rate, string RateType, string Amount, string AmountType, string BaseAmount, string BaseType, string? Day, string? DayType, string RateDayType,
        string FoundRate, string FoundRateType, string Base, int Scale);

    /// <summary> An expression of type <c>Decimal</c> for <paramref name="value"/> of Rust type <paramref name="type"/>. </summary>
    private static string ToDecimal(string value, string type) => type switch
    {
        "Decimal" => value,
        "f32" or "f64" => $"rust_decimal::Decimal::try_from({value}).unwrap_or_default()",
        _ => $"rust_decimal::Decimal::from({value})"
    };

    /// <summary> An expression of Rust type <paramref name="type"/> for <paramref name="value"/> of type <c>Decimal</c>. </summary>
    private static string FromDecimal(string value, string type) => type switch
    {
        "Decimal" => value,
        "f32" => $"{value}.to_f32().unwrap_or_default()",
        "f64" => $"{value}.to_f64().unwrap_or_default()",
        "i16" => $"{value}.to_i16().unwrap_or_default()",
        "i64" => $"{value}.to_i64().unwrap_or_default()",
        _ => $"{value}.to_i32().unwrap_or_default()"
    };

    public static string Code(Names n)
    {
        string day = n.DayType is null ? "chrono::Local::now().date_naive()" : n.DayType == "NaiveDateTime" ? $"row.{n.Day}.date()" : $"row.{n.Day}";
        string storedDay = n.DayType is null ? "" : n.DayType == "NaiveDateTime" ? $" && stored.{n.Day}.date() == day" : $" && stored.{n.Day} == day";
        string rateDay = n.RateDayType == "NaiveDateTime" ? "day.and_hms_opt(0, 0, 0).unwrap_or_default()" : "day";
        string foundRate = n.FoundRateType == n.RateType ? $"found.{n.FoundRate}" : FromDecimal(ToDecimal($"found.{n.FoundRate}", n.FoundRateType), n.RateType);
        string one = n.RateType switch { "Decimal" => "rust_decimal::Decimal::ONE", "f32" or "f64" => "1.0", _ => "1" };
        bool decimals = new[] { n.AmountType, n.RateType, n.BaseType, n.FoundRateType }.Contains("Decimal");
        string rounded = $"({ToDecimal("row." + n.Amount, n.AmountType)} * {ToDecimal("row." + n.Rate, n.RateType)}).round_dp_with_strategy({n.Scale}, rust_decimal::RoundingStrategy::MidpointAwayFromZero)";
        string baseValue = decimals ? FromDecimal(rounded, n.BaseType)
            : $"((row.{n.Amount} as f64 * row.{n.Rate} as f64 * 10f64.powi({n.Scale})).round() / 10f64.powi({n.Scale})) as {n.BaseType}";
        if (!decimals)
            foundRate = n.FoundRateType == n.RateType ? $"found.{n.FoundRate}" : $"found.{n.FoundRate} as {n.RateType}";
        return TemplateText.FillAll(Text, ("§STRUCT", n.Struct), ("§CUR", n.Currency), ("§RATE", n.Rate), ("§BASEAMT", n.BaseAmount), ("§DAYVALUE", day), ("§STOREDDAY", storedDay), ("§RATEDAY", rateDay),
            ("§FOUNDRATE", foundRate), ("§ONE", one), ("§BASEVALUE", baseValue), ("§DECIMALUSE", decimals ? "    #[allow(unused_imports)]\n    use rust_decimal::prelude::ToPrimitive as _;\n" : ""), ("§BASE", n.Base),
            ("§INVALIDTYPE", RateWords.InvalidType), ("§INVALIDTITLE", RateWords.InvalidTitle), ("§W_DOCNEEDS", RateWords.DocumentNeedsCurrency),
            ("§NEEDEDTYPE", RateWords.NeededType), ("§NEEDEDTITLE", RateWords.NeededTitle));
    }

    private const string Text = """"
/// The currency is trimmed and upper case. The rate is the one in force on the day (1 for §BASE), fixed when the row is created and kept while the currency and day stay; the converted amount is the amount times the rate.
async fn convert_currency(pool: &Pool, row: &mut §STRUCT, stored: Option<&§STRUCT>) -> ApiResult<()> {
§DECIMALUSE    row.§CUR = row.§CUR.trim().to_uppercase();
    if row.§CUR.is_empty() {
        return Err(ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", "§W_DOCNEEDS".into()));
    }
    let day = §DAYVALUE;
    match stored {
        Some(stored) if stored.§CUR.trim().to_uppercase() == row.§CUR§STOREDDAY => row.§RATE = stored.§RATE.clone(),
        _ if row.§CUR == "§BASE" => row.§RATE = §ONE,
        _ => {
            let (found, _) = find_rate(pool, &row.§CUR, "§BASE", §RATEDAY).await?;
            let Some(found) = found else {
                return Err(ApiError::Rejected(400, "§NEEDEDTYPE", "§NEEDEDTITLE", format!("There is no exchange rate from {} to §BASE on or before {}.", row.§CUR, day.format("%Y-%m-%d"))));
            };
            row.§RATE = §FOUNDRATE;
        }
    }
    row.§BASEAMT = §BASEVALUE;
    Ok(())
}
"""";
}
