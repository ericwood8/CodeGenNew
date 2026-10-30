using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Rust (axum, sqlx) counterpart of the effective-dating part of <see cref="RowShapeWriter"/>: the period check and <c>GET current</c> of a table listed in <c>EffectiveDatedTables</c>, written into the table's own router (RS_Routes). </summary>
public static class RustEffectiveWriter
{
    /// <summary> A group column: the struct field, its JSON name, the database column, the type without <c>Option</c> and whether the field is optional. </summary>
    public sealed record Group(string Field, string Json, string Column, string Type, bool IsOption);

    /// <summary> What the code needs of the table: struct, key and period fields, the date type, the group columns and the select list of the struct. </summary>
    public sealed record Names(string Struct, string KeyField, string KeyColumn, string KeyType, string FromField, string FromColumn, string ToField, string ToColumn, bool ToIsOption, string DateType,
        IReadOnlyList<Group> Groups, string Table, string SelectList);

    /// <summary> The route line to add to the router. </summary>
    public static string RouteLine(string route, string readLayer) => $"        .route(\"{route}/current\", get(current){readLayer})\n";

    /// <summary> <c>check_effective</c>, the query struct and <c>current</c>. <paramref name="liveAnd"/> is the soft-delete test (<c> AND flag = false</c>) or empty. </summary>
    public static string Code(Names n, string liveAnd)
    {
        var checkGroups = new StringBuilder();
        var queryFields = new StringBuilder();
        var currentGroups = new StringBuilder();
        foreach (var g in n.Groups)
        {
            string column = g.Column;
            checkGroups.Append(g.IsOption
                ? $"    match &row.{g.Field} {{\n        Some(value) => {{\n            builder.push({RustTable.Raw($" AND {column} = ")}).push_bind(value.clone());\n        }}\n        None => {{\n            builder.push({RustTable.Raw($" AND {column} IS NULL")});\n        }}\n    }}\n"
                : $"    builder.push({RustTable.Raw($" AND {column} = ")}).push_bind(row.{g.Field}.clone());\n");
            queryFields.Append($"    #[serde(rename = \"{g.Json}\")]\n    {g.Field}: Option<{Qualify(g.Type)}>,\n");
            currentGroups.Append($"    if let Some(value) = query.{g.Field} {{\n        builder.push({RustTable.Raw($" AND {column} = ")}).push_bind(value);\n    }}\n");
        }
        string today = n.DateType == "NaiveDateTime" ? "chrono::Local::now().date_naive().and_hms_opt(0, 0, 0).unwrap_or_default()" : "chrono::Local::now().date_naive()";
        string to = n.ToIsOption ? $"row.{n.ToField}" : $"Some(row.{n.ToField})";
        return Fill(Text, ("§STRUCT", n.Struct), ("§KEYFIELD", n.KeyField), ("§KEYCOLUMN", n.KeyColumn), ("§KT", n.KeyType), ("§FROMFIELD", n.FromField), ("§FROMCOLUMN", n.FromColumn), ("§TOFIELD", n.ToField), ("§TOCOLUMN", n.ToColumn),
            ("§TOVALUE", to), ("§DATE", Qualify(n.DateType)), ("§TODAY", today), ("§TABLE", n.Table), ("§SELECT", n.SelectList), ("§LIVE", liveAnd), ("§CHECKGROUPS", checkGroups.ToString()), ("§QUERYFIELDS", queryFields.ToString()),
            ("§CURRENTGROUPS", currentGroups.ToString()), ("§RANGETYPE", RowShapeWords.EffectiveRangeType), ("§OVERLAPTYPE", RowShapeWords.EffectiveOverlapType),
            ("§W_RANGE", RowShapeWords.BackwardsRange), ("§W_OVERLAP", RowShapeWords.Overlap));
    }

    /// <summary> A type as the router can write it without an import of its own. </summary>
    private static string Qualify(string type) => type switch
    {
        "NaiveDate" or "NaiveDateTime" or "NaiveTime" => "chrono::" + type,
        "DateTime<Utc>" => "chrono::DateTime<chrono::Utc>",
        "Uuid" => "uuid::Uuid",
        "Decimal" => "rust_decimal::Decimal",
        _ => type
    };

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string Text = """"
/// The period must not run backwards and must not overlap another row of the same group (both ends are inclusive; an empty end is open).
async fn check_effective(pool: &Pool, row_id: Option<§KT>, row: &§STRUCT) -> ApiResult<()> {
    let to = §TOVALUE;
    if to.is_some_and(|end| end < row.§FROMFIELD) {
        return Err(ApiError::Rejected(400, "§RANGETYPE", "Not a valid period", "§W_RANGE".into()));
    }
    let mut builder = sqlx::QueryBuilder::<Db>::new(r#"SELECT COUNT(*) FROM §TABLE WHERE 1 = 1§LIVE"#);
    if let Some(id) = row_id {
        builder.push(r#" AND §KEYCOLUMN <> "#).push_bind(id);
    }
§CHECKGROUPS    if let Some(end) = to {
        builder.push(r#" AND §FROMCOLUMN <= "#).push_bind(end);
    }
    builder.push(r#" AND (§TOCOLUMN IS NULL OR §TOCOLUMN >= "#).push_bind(row.§FROMFIELD).push(")");
    if builder.build_query_scalar::<i64>().fetch_one(pool).await? > 0 {
        return Err(ApiError::Rejected(409, "§OVERLAPTYPE", "Overlapping period", "§W_OVERLAP".into()));
    }
    Ok(())
}

#[derive(serde::Deserialize)]
struct CurrentQuery {
    on: Option<§DATE>,
§QUERYFIELDS}

/// The rows in effect on a day (today when none is given), narrowed by the group columns that are given.
async fn current(State(pool): State<Pool>, Query(query): Query<CurrentQuery>) -> ApiResult<Json<Vec<§STRUCT>>> {
    let day = query.on.unwrap_or_else(|| §TODAY);
    let mut builder = sqlx::QueryBuilder::<Db>::new(r#"SELECT §SELECT FROM §TABLE WHERE §FROMCOLUMN <= "#);
    builder.push_bind(day).push(r#" AND (§TOCOLUMN IS NULL OR §TOCOLUMN >= "#).push_bind(day).push(r#")§LIVE"#);
§CURRENTGROUPS    builder.push(r#" ORDER BY §FROMCOLUMN DESC, §KEYCOLUMN"#);
    Ok(Json(builder.build_query_as::<§STRUCT>().fetch_all(&pool).await?))
}

"""";
}
