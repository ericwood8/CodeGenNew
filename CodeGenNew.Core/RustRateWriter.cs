namespace CodeGenNew.Core;

/// <summary> The Rust (axum, sqlx) counterpart of the rate part of <see cref="RowShapeWriter"/> (<c>RateTables</c>): the table's router gets <c>check_rate</c>, called before a create or an update writes the row, and
/// <c>GET rate</c>. The words are <see cref="RateWords"/>. </summary>
public static class RustRateWriter
{
    /// <summary> The rate table: its struct, table, key column and type, the currency, rate and day as struct fields and database columns (quoted), the type of the day without <c>Option</c> and the zero of the rate's type. </summary>
    public sealed record Names(string Struct, string Table, string KeyColumn, string KeyType, string FromField, string FromColumn, string ToField, string ToColumn, string RateField, string RateZero,
        string DayField, string DayColumn, string DateType, string SelectList, bool Inverse = false, string RateOne = "1.0", string KeyField = "");

    /// <summary> The one a rate is divided into for the inverse, by the Rust type of its column. </summary>
    public static string OneOf(string baseType) => baseType switch { "Decimal" => "rust_decimal::Decimal::ONE", _ => "1.0" };

    public static string RouteLine(string route, string readLayer) => $"        .route(\"{route}/rate\", get(rate_on){readLayer})\n";

    /// <summary> The zero a rate is compared with, by the Rust type of its column. </summary>
    public static string ZeroOf(string baseType) => baseType switch { "f32" or "f64" => "0.0", "Decimal" => "rust_decimal::Decimal::ZERO", _ => "0" };

    /// <summary> The insert of a rate row: a save the database refused goes through <c>rate_refused</c>. </summary>
    public const string InsertLines = "    let row = match repo::insert(&pool, &row).await {\n        Ok(saved) => saved,\n        Err(error) => return Err(rate_refused(&pool, None, &mut row, error).await),\n    };";

    /// <summary> The update of a rate row, the last expression of the handler. </summary>
    public static string UpdateLines(string idArgument) =>
        $"    match repo::update(&pool, {idArgument}, &row).await {{\n        Ok(saved) => saved.map(Json).ok_or(ApiError::NotFound),\n        Err(error) => Err(rate_refused(&pool, Some(id), &mut row, error).await),\n    }}";

    public static string Code(Names n)
    {
        bool dateTime = n.DateType == "NaiveDateTime";
        string date = "chrono::" + n.DateType;
        string today = dateTime ? "chrono::Local::now().date_naive().and_hms_opt(0, 0, 0).unwrap_or_default()" : "chrono::Local::now().date_naive()";
        string sameDay = dateTime
            ? $"    let start = row.{n.DayField}.date().and_hms_opt(0, 0, 0).unwrap_or_default();\n    builder.push({RustTable.Raw($" AND {n.DayColumn} >= ")}).push_bind(start).push({RustTable.Raw($" AND {n.DayColumn} < ")}).push_bind(start + chrono::Duration::days(1));\n"
            : $"    builder.push({RustTable.Raw($" AND {n.DayColumn} = ")}).push_bind(row.{n.DayField});\n";
        string onOrBefore = dateTime
            ? $"    let end = day.date().and_hms_opt(0, 0, 0).unwrap_or_default() + chrono::Duration::days(1);\n    builder.push({RustTable.Raw($" AND {n.DayColumn} < ")}).push_bind(end);\n"
            : $"    builder.push({RustTable.Raw($" AND {n.DayColumn} <= ")}).push_bind(day);\n";
        string dayLine = dateTime
            ? "    let day = match query.on.as_deref() {\n        Some(text) => parse_rate_day(text).ok_or_else(|| ApiError::BadRequest(\"Give the day as yyyy-mm-dd.\".into()))?,\n        None => " + today + ",\n    };\n"
            : "    let day = query.on.unwrap_or_else(|| " + today + ");\n";
        string inverse = n.Inverse ? TemplateText.Fill(InverseText, ("§ONORBEFORE", onOrBefore), ("§STRUCT", n.Struct), ("§DAYFIELD", n.DayField), ("§FROMFIELD", n.FromField), ("§TOFIELD", n.ToField),
            ("§RATEFIELD", n.RateField), ("§ONEOF", n.RateOne), ("§KEYFIELD", n.KeyField),
            ("§SELECT", RustTable.Raw($"SELECT {n.SelectList} FROM {n.Table} WHERE UPPER({n.FromColumn}) = ")), ("§ANDTO", RustTable.Raw($" AND UPPER({n.ToColumn}) = ")), ("§ORDER", RustTable.Raw($" ORDER BY {n.DayColumn} DESC, {n.KeyColumn} DESC LIMIT 1"))) + "\n" : "";
        string answer = n.Inverse
            ? "    let mut response = axum::response::IntoResponse::into_response(Json(row));\n    if inverted {\n        response.headers_mut().insert(\"" + RateWords.InvertedHeader + "\", axum::http::HeaderValue::from_static(\"true\"));\n    }\n    Ok(response)"
            : "    Ok(Json(row))";
        return TemplateText.FillAll(Text, ("§INVERSE", inverse), ("§ANSWER", answer), ("§RETURN", n.Inverse ? "axum::response::Response" : "Json<" + n.Struct + ">"), ("§STRUCT", n.Struct), ("§KT", n.KeyType), ("§FROMFIELD", n.FromField), ("§TOFIELD", n.ToField), ("§RATEFIELD", n.RateField), ("§ZERO", n.RateZero), ("§DATE", date), ("§TODAY", today),
            ("§COUNT", RustTable.Raw($"SELECT COUNT(*) FROM {n.Table} WHERE UPPER({n.FromColumn}) = ")), ("§ANDTO", RustTable.Raw($" AND UPPER({n.ToColumn}) = ")), ("§ANDKEY", RustTable.Raw($" AND {n.KeyColumn} <> ")),
            ("§SELECT", RustTable.Raw($"SELECT {n.SelectList} FROM {n.Table} WHERE UPPER({n.FromColumn}) = ")), ("§ORDER", RustTable.Raw($" ORDER BY {n.DayColumn} DESC, {n.KeyColumn} DESC LIMIT 1")),
            ("§SAMEDAY", sameDay), ("§ONORBEFORE", onOrBefore), ("§ONTYPE", dateTime ? "String" : date), ("§DAYLINE", dayLine), ("§PARSER", dateTime ? DayParser : ""),
            ("§INVALIDTYPE", RateWords.InvalidType), ("§INVALIDTITLE", RateWords.InvalidTitle), ("§TAKENTYPE", RateWords.TakenType), ("§TAKENTITLE", RateWords.TakenTitle),
            ("§MISSINGTYPE", RateWords.MissingType), ("§MISSINGTITLE", RateWords.MissingTitle),
            ("§W_NEEDS", RateWords.NeedsCurrencies), ("§W_SAME", RateWords.SameCurrency), ("§W_POSITIVE", RateWords.NotPositive), ("§W_TAKEN", RateWords.Taken), ("§W_PAIR", RateWords.NeedsPair));
    }

    /// <summary> A day given as a date or a date and time (the query of a date-and-time column answers both). </summary>
    private const string DayParser = """"
/// A day as a date (the start of that day) or a date and time.
fn parse_rate_day(text: &str) -> Option<chrono::NaiveDateTime> {
    let text = text.trim();
    chrono::NaiveDateTime::parse_from_str(text, "%Y-%m-%dT%H:%M:%S").ok()
        .or_else(|| chrono::NaiveDateTime::parse_from_str(text, "%Y-%m-%dT%H:%M:%S%.f").ok())
        .or_else(|| chrono::NaiveDateTime::parse_from_str(text, "%Y-%m-%d %H:%M:%S").ok())
        .or_else(|| chrono::NaiveDate::parse_from_str(text, "%Y-%m-%d").ok().and_then(|d| d.and_hms_opt(0, 0, 0)))
}

"""";

    private const string InverseText = """"
    let mut builder = sqlx::QueryBuilder::<Db>::new(§SELECT);
    builder.push_bind(target.to_string()).push(§ANDTO).push_bind(source.to_string());
§ONORBEFORE    builder.push(§ORDER);
    if let Some(mut other) = builder.build_query_as::<§STRUCT>().fetch_optional(pool).await? {
        if row.as_ref().map_or(true, |direct| other.§DAYFIELD > direct.§DAYFIELD) {
            std::mem::swap(&mut other.§FROMFIELD, &mut other.§TOFIELD);
            other.§RATEFIELD = §ONEOF / other.§RATEFIELD;
            other.§KEYFIELD = Default::default();
            return Ok((Some(other), true));
        }
    }
"""";

    private const string Text = """"
/// The currencies are trimmed and upper case and differ, the rate is above zero, and no other row holds a rate for the pair on the same day (409).
async fn check_rate(pool: &Pool, row_id: Option<§KT>, row: &mut §STRUCT) -> ApiResult<()> {
    row.§FROMFIELD = row.§FROMFIELD.trim().to_uppercase();
    row.§TOFIELD = row.§TOFIELD.trim().to_uppercase();
    if row.§FROMFIELD.is_empty() || row.§TOFIELD.is_empty() {
        return Err(ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", "§W_NEEDS".into()));
    }
    if row.§FROMFIELD == row.§TOFIELD {
        return Err(ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", "§W_SAME".into()));
    }
    if row.§RATEFIELD <= §ZERO {
        return Err(ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", "§W_POSITIVE".into()));
    }
    let mut builder = sqlx::QueryBuilder::<Db>::new(§COUNT);
    builder.push_bind(row.§FROMFIELD.clone()).push(§ANDTO).push_bind(row.§TOFIELD.clone());
§SAMEDAY    if let Some(id) = row_id {
        builder.push(§ANDKEY).push_bind(id);
    }
    if builder.build_query_scalar::<i64>().fetch_one(pool).await? > 0 {
        return Err(ApiError::Rejected(409, "§TAKENTYPE", "§TAKENTITLE", "§W_TAKEN".into()));
    }
    Ok(())
}

/// The answer to a save the database refused: a rate another row holds by now (two saves at the same moment, stopped by the unique index) answers as the rule does.
async fn rate_refused(pool: &Pool, row_id: Option<§KT>, row: &mut §STRUCT, error: sqlx::Error) -> ApiError {
    let unique = matches!(&error, sqlx::Error::Database(database) if database.kind() == sqlx::error::ErrorKind::UniqueViolation);
    if unique {
        if let Err(taken) = check_rate(pool, row_id, row).await {
            return taken;
        }
    }
    error.into()
}

§PARSER#[derive(serde::Deserialize)]
struct RateQuery {
    from: Option<String>,
    to: Option<String>,
    on: Option<§ONTYPE>,
}

/// The row in force for the pair on a day: the latest one on or before it (the second value is true when it is worked out from the opposite pair).
pub async fn find_rate(pool: &Pool, source: &str, target: &str, day: §DATE) -> ApiResult<(Option<§STRUCT>, bool)> {
    let mut builder = sqlx::QueryBuilder::<Db>::new(§SELECT);
    builder.push_bind(source.to_string()).push(§ANDTO).push_bind(target.to_string());
§ONORBEFORE    builder.push(§ORDER);
    let row = builder.build_query_as::<§STRUCT>().fetch_optional(pool).await?;
§INVERSE    Ok((row, false))
}

/// The rate in force on a day (today when none is given): the latest row of the pair on or before it.
async fn rate_on(State(pool): State<Pool>, Query(query): Query<RateQuery>) -> ApiResult<§RETURN> {
    let source = query.from.unwrap_or_default().trim().to_uppercase();
    let target = query.to.unwrap_or_default().trim().to_uppercase();
    if source.is_empty() || target.is_empty() {
        return Err(ApiError::BadRequest("§W_PAIR".into()));
    }
    if source == target {
        return Err(ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", "§W_SAME".into()));
    }
§DAYLINE    let (found, inverted) = find_rate(&pool, &source, &target, day).await?;
    let Some(row) = found else {
        return Err(ApiError::Rejected(404, "§MISSINGTYPE", "§MISSINGTITLE", format!("There is no rate from {source} to {target} on or before that day.")));
    };
    let _ = inverted;
§ANSWER
}

"""";
}
