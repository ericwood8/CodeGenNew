namespace CodeGenNew.Core;

/// <summary> The Rust (axum, sqlx) counterpart of the party part of <see cref="RowShapeWriter"/> (<c>PartyTables</c>): the identifier table's router gets <c>check_identifier</c>, called before a create or an update writes
/// the row, and the party table's router gets <c>GET by-identifier</c>. The words are <see cref="PartyWords"/>. </summary>
public static class RustPartyWriter
{
    /// <summary> The identifier table: its struct, table, key column and type, and the scheme and value as struct fields and database columns (quoted). </summary>
    public sealed record IdentifierNames(string Struct, string Table, string KeyColumn, string KeyType, string SchemeField, string SchemeColumn, string ValueField, string ValueColumn);

    /// <summary> The party table (struct, table, select list, key column) and its identifier table (table, link, scheme and value columns); every database name is quoted. </summary>
    public sealed record PartyNames(string Struct, string Table, string SelectList, string KeyColumn, string IdentifierTable, string LinkColumn, string SchemeColumn, string ValueColumn);

    /// <summary> The route line to add to the party table's router. </summary>
    public static string RouteLine(string route, string readLayer) => $"        .route(\"{route}/by-identifier\", get(by_identifier){readLayer})\n";

    public static string CheckCode(IdentifierNames n) => TemplateText.FillAll(CheckText, ("§STRUCT", n.Struct), ("§KT", n.KeyType), ("§SCHEMEFIELD", n.SchemeField), ("§VALUEFIELD", n.ValueField),
        ("§COUNT", RustTable.Raw($"SELECT COUNT(*) FROM {n.Table} WHERE LOWER({n.SchemeColumn}) = ")), ("§ANDVALUE", RustTable.Raw($" AND LOWER({n.ValueColumn}) = ")), ("§ANDKEY", RustTable.Raw($" AND {n.KeyColumn} <> ")),
        ("§TYPE", PartyWords.IdentifierTakenType), ("§TITLE", PartyWords.IdentifierTakenTitle), ("§W_TAKEN", PartyWords.IdentifierTaken));

    /// <summary> The insert of an identifier row: a save the database refused goes through <c>identifier_refused</c>. </summary>
    public const string InsertLines = "    let row = match repo::insert(&pool, &row).await {\n        Ok(saved) => saved,\n        Err(error) => return Err(identifier_refused(&pool, None, &mut row, error).await),\n    };";

    /// <summary> The update of an identifier row, the last expression of the handler. </summary>
    public static string UpdateLines(string idArgument) =>
        $"    match repo::update(&pool, {idArgument}, &row).await {{\n        Ok(saved) => saved.map(Json).ok_or(ApiError::NotFound),\n        Err(error) => Err(identifier_refused(&pool, Some(id), &mut row, error).await),\n    }}";

    public static string FindCode(PartyNames n) => TemplateText.FillAll(FindText, ("§STRUCT", n.Struct),
        ("§SELECT", RustTable.Raw($"SELECT {n.SelectList} FROM {n.Table} WHERE {n.KeyColumn} IN (SELECT {n.LinkColumn} FROM {n.IdentifierTable} WHERE LOWER({n.ValueColumn}) = ")),
        ("§ANDSCHEME", RustTable.Raw($" AND LOWER({n.SchemeColumn}) = ")), ("§END", RustTable.Raw($") ORDER BY {n.KeyColumn} LIMIT {PartyWords.FindLimit}")),
        ("§LIMIT", PartyWords.FindLimit.ToString()), ("§W_VALUE", PartyWords.ValueNeeded));

    private const string CheckText = """"
/// A scheme and value pair is held by one row only: both are trimmed, and a pair another row holds (compared without regard to case) is refused (409).
async fn check_identifier(pool: &Pool, row_id: Option<§KT>, row: &mut §STRUCT) -> ApiResult<()> {
    row.§SCHEMEFIELD = row.§SCHEMEFIELD.trim().to_string();
    row.§VALUEFIELD = row.§VALUEFIELD.trim().to_string();
    let mut builder = sqlx::QueryBuilder::<Db>::new(§COUNT);
    builder.push_bind(row.§SCHEMEFIELD.to_lowercase()).push(§ANDVALUE).push_bind(row.§VALUEFIELD.to_lowercase());
    if let Some(id) = row_id {
        builder.push(§ANDKEY).push_bind(id);
    }
    if builder.build_query_scalar::<i64>().fetch_one(pool).await? > 0 {
        return Err(ApiError::Rejected(409, "§TYPE", "§TITLE", "§W_TAKEN".into()));
    }
    Ok(())
}

/// The answer to a save the database refused: a pair another row holds by now (two saves at the same moment, stopped by the unique index) answers as the rule does.
async fn identifier_refused(pool: &Pool, row_id: Option<§KT>, row: &mut §STRUCT, error: sqlx::Error) -> ApiError {
    let unique = matches!(&error, sqlx::Error::Database(database) if database.kind() == sqlx::error::ErrorKind::UniqueViolation);
    if unique {
        if let Err(taken) = check_identifier(pool, row_id, row).await {
            return taken;
        }
    }
    error.into()
}

"""";

    private const string FindText = """"
#[derive(serde::Deserialize)]
struct IdentifierQuery {
    value: Option<String>,
    scheme: Option<String>,
}

/// The rows that hold an identifier with this value (of this scheme, when one is given), compared without regard to case; at most §LIMIT.
async fn by_identifier(State(pool): State<Pool>, Query(query): Query<IdentifierQuery>) -> ApiResult<Json<Vec<§STRUCT>>> {
    let value = query.value.unwrap_or_default().trim().to_lowercase();
    if value.is_empty() {
        return Err(ApiError::BadRequest("§W_VALUE".into()));
    }
    let mut builder = sqlx::QueryBuilder::<Db>::new(§SELECT);
    builder.push_bind(value);
    if let Some(scheme) = query.scheme.map(|s| s.trim().to_lowercase()).filter(|s| !s.is_empty()) {
        builder.push(§ANDSCHEME).push_bind(scheme);
    }
    builder.push(§END);
    Ok(Json(builder.build_query_as::<§STRUCT>().fetch_all(&pool).await?))
}

"""";
}
