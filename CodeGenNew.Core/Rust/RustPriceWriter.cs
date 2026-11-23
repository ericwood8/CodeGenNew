using CodeGenNew.Core.Price;
using CodeGenNew.Core.Sql;

namespace CodeGenNew.Core.Rust;

/// <summary> The Rust (axum, sqlx) counterpart of the price part of <see cref="RowShapeWriter"/> (<c>PriceListTables</c>): the price table's router gets <c>check_price</c>, called before a create or an update writes the row,
/// and <c>GET price</c>. The words are <see cref="PriceWords"/>. </summary>
public static class RustPriceWriter
{
    /// <summary> The price table and the list table: struct, table, columns (quoted) and fields, the Rust types of the item and the price list and the zeros the minimum quantity and the price are compared with.
    /// <paramref name="GroupColumn"/> is null when the entry names no customer group. </summary>
    public sealed record Names(string Struct, string Table, string SelectList, string KeyColumn, string KeyType, string ListField, string ListColumn, string ItemField, string ItemColumn, string ItemType, string MinField, string MinColumn,
        string MinZero, string PriceField, string PriceZero, string ListTable, string ListKeyColumn, string CurrencyColumn, string FromColumn, string ToColumn, string? GroupColumn, string KeyField = "");

    /// <summary> The names of <paramref name="plan"/> for the price table <paramref name="t"/> and its list table, or null without a plan. </summary>
    public static Names? NamesFor(PriceListShape? plan, RustTable t, RustTable? listTable, DialectInfo dialect, string keyType)
    {
        if (plan is null || listTable is null)
            return null;
        return new Names(t.Struct, t.Table, t.SelectList, dialect.Quote(plan.Key.DbName), keyType, t.FieldOf(plan.List).Name, dialect.Quote(plan.List.DbName), t.FieldOf(plan.Item).Name, dialect.Quote(plan.Item.DbName), t.FieldOf(plan.Item).BaseType,
            t.FieldOf(plan.MinQuantity).Name, dialect.Quote(plan.MinQuantity.DbName), RustRateWriter.ZeroOf(t.FieldOf(plan.MinQuantity).BaseType), t.FieldOf(plan.Price).Name, RustRateWriter.ZeroOf(t.FieldOf(plan.Price).BaseType),
            listTable.Table, dialect.Quote(plan.ListKey.DbName), dialect.Quote(plan.Currency.DbName), dialect.Quote(plan.ValidFrom.DbName), dialect.Quote(plan.ValidTo.DbName), plan.Group is null ? null : dialect.Quote(plan.Group.DbName), t.FieldOf(plan.Key).Name);
    }

    /// <summary> The lines before <c>check_price</c> for a row with an owner column of its own whose list has one too: the list must belong to the row's owner (400 otherwise). </summary>
    public static string OwnerLines(Names n, string listModule, string listOwnerField, string rowOwnerField) =>
        $"    if !crate::repos::{listModule}::get_by_id(&pool, row.{n.ListField}).await?.is_some_and(|l| l.{listOwnerField} == row.{rowOwnerField}) {{\n"
        + $"        return Err(ApiError::Rejected(400, \"{PriceWords.InvalidType}\", \"{PriceWords.InvalidTitle}\", \"{PriceWords.ListMissing}\".into()));\n    }}";

    /// <summary> The lines before <c>check_price</c> for a table limited to a tenant: the list the row names must be one of the caller's tenant (400 otherwise). </summary>
    public static string ListLines(Names n, string listModule, string listKeyField, string tenancyArgument) =>
        $"    if !crate::repos::{listModule}::get_all_scoped(&pool, {tenancyArgument}).await?.iter().any(|l| l.{listKeyField} == row.{n.ListField}) {{\n"
        + $"        return Err(ApiError::Rejected(400, \"{PriceWords.InvalidType}\", \"{PriceWords.InvalidTitle}\", \"{PriceWords.ListMissing}\".into()));\n    }}";

    public static string RouteLine(string route, string readLayer) => $"        .route(\"{route}/price\", get(price_on){readLayer})\n";

    /// <summary> The insert of a price row: a save the database refused goes through <c>price_refused</c>. </summary>
    public const string InsertLines = "    let row = match repo::insert(&pool, &row).await {\n        Ok(saved) => saved,\n        Err(error) => return Err(price_refused(&pool, None, &mut row, error).await),\n    };";

    /// <summary> The update of a price row, the last expression of the handler. </summary>
    public static string UpdateLines(string idArgument) =>
        $"    match repo::update(&pool, {idArgument}, &row).await {{\n        Ok(saved) => saved.map(Json).ok_or(ApiError::NotFound),\n        Err(error) => Err(price_refused(&pool, Some(id), &mut row, error).await),\n    }}";

    /// <summary> <paramref name="userArgument"/> and <paramref name="filterArguments"/> are set for a table limited to an owner or a tenant: the keys the caller may see come from the repository's scoped read and the lookup is narrowed to them. </summary>
    public static string Code(Names n, string? userArgument = null, string? filterArguments = null)
    {
        string groupClause = n.GroupColumn is null ? "" : TemplateText.Fill(GroupText, ("§GROUPEQ", RustTable.Raw($" AND UPPER({n.GroupColumn}) = ")), ("§GROUPNULL", RustTable.Raw($" AND {n.GroupColumn} IS NULL")));
        string groupLine = n.GroupColumn is null ? "    let group: Option<String> = None;\n" : "    let group = query.group.map(|g| g.trim().to_uppercase()).filter(|g| !g.is_empty());\n";
        string allowed = filterArguments is null ? "None" : $"Some(repo::get_all_scoped(&pool{filterArguments}).await?.into_iter().map(|r| r.{n.KeyField}).collect())";
        return TemplateText.FillAll(Text, ("§ALLOWED", allowed), ("§USERARG", userArgument ?? ""), ("§ANDKEYIN", RustTable.Raw($" AND {n.KeyColumn} IN (")), ("§GROUPCLAUSE", groupClause), ("§GROUPLINE", groupLine), ("§GROUPFIELD", n.GroupColumn is null ? "" : "    group: Option<String>,\n"),
            ("§STRUCT", n.Struct), ("§KT", n.KeyType), ("§ITEMTYPE", n.ItemType), ("§LISTFIELD", n.ListField), ("§ITEMFIELD", n.ItemField), ("§MINFIELD", n.MinField), ("§PRICEFIELD", n.PriceField), ("§MINZERO", n.MinZero), ("§PRICEZERO", n.PriceZero),
            ("§COUNT", RustTable.Raw($"SELECT COUNT(*) FROM {n.Table} WHERE {n.ListColumn} = ")), ("§ANDITEMEQ", RustTable.Raw($" AND {n.ItemColumn} = ")), ("§ANDMINEQ", RustTable.Raw($" AND {n.MinColumn} = ")), ("§ANDKEY", RustTable.Raw($" AND {n.KeyColumn} <> ")),
            ("§SELECT", RustTable.Raw($"SELECT {n.SelectList} FROM {n.Table} WHERE {n.ItemColumn} = ")), ("§ANDMINLE", RustTable.Raw($" AND {n.MinColumn} <= ")),
            ("§INLIST", RustTable.Raw($" AND {n.ListColumn} IN (SELECT {n.ListKeyColumn} FROM {n.ListTable} WHERE UPPER({n.CurrencyColumn}) = ")), ("§ANDFROM", RustTable.Raw($" AND {n.FromColumn} <= ")),
            ("§ANDTO", RustTable.Raw($" AND ({n.ToColumn} IS NULL OR {n.ToColumn} >= ")), ("§CLOSE", RustTable.Raw($")")), ("§ENDLIST", RustTable.Raw(")")), ("§ORDERBY", RustTable.Raw($" ORDER BY {n.MinColumn} DESC, {n.KeyColumn} DESC LIMIT 1")),
            ("§INVALIDTYPE", PriceWords.InvalidType), ("§INVALIDTITLE", PriceWords.InvalidTitle), ("§TAKENTYPE", PriceWords.TakenType), ("§TAKENTITLE", PriceWords.TakenTitle),
            ("§MISSINGTYPE", PriceWords.MissingType), ("§MISSINGTITLE", PriceWords.MissingTitle), ("§MISSING", PriceWords.MissingFormat.Replace("{0}", "{}").Replace("{1}", "{}")),
            ("§W_MIN", PriceWords.QuantityNotPositive), ("§W_PRICE", PriceWords.PriceNegative), ("§W_TAKEN", PriceWords.Taken), ("§W_LOOKUP", PriceWords.NeedsLookup), ("§W_QTY", PriceWords.AskedQuantity));
    }

    private const string GroupText = """"
        match held {
            Some(group) => {
                builder.push(§GROUPEQ).push_bind(group.to_string());
            }
            None => {
                builder.push(§GROUPNULL);
            }
        }
"""";

    private const string Text = """"
/// The minimum quantity is above zero, the price is not negative, and no other row of the list holds a price for the item from the same quantity (409).
async fn check_price(pool: &Pool, row_id: Option<§KT>, row: &mut §STRUCT) -> ApiResult<()> {
    if row.§MINFIELD <= §MINZERO {
        return Err(ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", "§W_MIN".into()));
    }
    if row.§PRICEFIELD < §PRICEZERO {
        return Err(ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", "§W_PRICE".into()));
    }
    let mut builder = sqlx::QueryBuilder::<Db>::new(§COUNT);
    builder.push_bind(row.§LISTFIELD.clone()).push(§ANDITEMEQ).push_bind(row.§ITEMFIELD.clone()).push(§ANDMINEQ).push_bind(row.§MINFIELD.clone());
    if let Some(id) = row_id {
        builder.push(§ANDKEY).push_bind(id);
    }
    if builder.build_query_scalar::<i64>().fetch_one(pool).await? > 0 {
        return Err(ApiError::Rejected(409, "§TAKENTYPE", "§TAKENTITLE", "§W_TAKEN".into()));
    }
    Ok(())
}

/// The answer to a save the database refused: a price another row holds by now (two saves at the same moment, stopped by the unique index) answers as the rule does.
async fn price_refused(pool: &Pool, row_id: Option<§KT>, row: &mut §STRUCT, error: sqlx::Error) -> ApiError {
    let unique = matches!(&error, sqlx::Error::Database(database) if database.kind() == sqlx::error::ErrorKind::UniqueViolation);
    if unique {
        if let Err(taken) = check_price(pool, row_id, row).await {
            return taken;
        }
    }
    error.into()
}

#[derive(serde::Deserialize)]
struct PriceQuery {
    item: Option<§ITEMTYPE>,
    quantity: Option<f64>,
    on: Option<chrono::NaiveDate>,
    currency: Option<String>,
§GROUPFIELD}

/// The row in force: of the lists in the currency that are valid on the day the row of the item with the largest minimum quantity not above the quantity. A group's lists come before the lists with no group.
pub async fn find_price(pool: &Pool, item: §ITEMTYPE, quantity: f64, day: chrono::NaiveDate, currency: &str, group: Option<&str>, allowed: Option<&[§KT]>) -> ApiResult<Option<§STRUCT>> {
    if allowed.is_some_and(|keys| keys.is_empty()) {
        return Ok(None);
    }
    let tries: Vec<Option<&str>> = match group {
        Some(name) => vec![Some(name), None],
        None => vec![None],
    };
    for held in tries {
        let mut builder = sqlx::QueryBuilder::<Db>::new(§SELECT);
        builder.push_bind(item).push(§ANDMINLE).push_bind(quantity).push(§INLIST).push_bind(currency.to_string());
        builder.push(§ANDFROM).push_bind(day).push(§ANDTO).push_bind(day).push(§CLOSE);
§GROUPCLAUSE        let _ = &held;
        builder.push(§ENDLIST);
        if let Some(keys) = allowed {
            builder.push(§ANDKEYIN);
            let mut list = builder.separated(", ");
            for key in keys {
                list.push_bind(key.clone());
            }
            list.push_unseparated(")");
        }
        builder.push(§ORDERBY);
        if let Some(row) = builder.build_query_as::<§STRUCT>().fetch_optional(pool).await? {
            return Ok(Some(row));
        }
    }
    Ok(None)
}

/// The price in force for an item and a quantity (1 when none is given) on a day (today when none is given) in a currency.
async fn price_on(State(pool): State<Pool>, Query(query): Query<PriceQuery>§USERARG) -> ApiResult<Json<§STRUCT>> {
    let currency = query.currency.clone().unwrap_or_default().trim().to_uppercase();
    let (Some(item), false) = (query.item, currency.is_empty()) else {
        return Err(ApiError::BadRequest("§W_LOOKUP".into()));
    };
    let quantity = query.quantity.unwrap_or(1.0);
    if quantity <= 0.0 {
        return Err(ApiError::Rejected(400, "§INVALIDTYPE", "§INVALIDTITLE", "§W_QTY".into()));
    }
    let day = query.on.unwrap_or_else(|| chrono::Local::now().date_naive());
§GROUPLINE    let allowed: Option<Vec<§KT>> = §ALLOWED;
    let Some(row) = find_price(&pool, item, quantity, day, &currency, group.as_deref(), allowed.as_deref()).await? else {
        return Err(ApiError::Rejected(404, "§MISSINGTYPE", "§MISSINGTITLE", format!("§MISSING", item, currency)));
    };
    Ok(Json(row))
}

"""";
}
