using CodeGenNew.Core.Catalog;
using CodeGenNew.Core.Sql;

namespace CodeGenNew.Core.Rust;

/// <summary> The Rust (axum, sqlx) counterpart of <see cref="CatalogWriter"/> (<c>FacetTables</c>): the product table's router gets <c>GET facets</c> and <c>GET narrow</c>. The query is built with sqlx's query builder, so the placeholders and the quotes follow the database.
/// Several values of one attribute are alternatives, different attributes all have to match, and an attribute is counted without its own filter. The keyword lowers both sides, so the answer does not depend on the collation. </summary>
public static class RustCatalogWriter
{
    public static string RouteLines(string route, string layer) =>
        $"        .route(\"{route}/{CatalogPlan.FacetsRoute}\", get(catalog_facets){layer})\n"
        + $"        .route(\"{route}/{CatalogPlan.NarrowRoute}\", get(catalog_narrow){layer})\n";

    public static string Code(CatalogShape s, RustTable product, ProjectSettings project, DialectInfo di)
    {
        var attributes = RustTable.Of(s.Attributes, project);
        string Q(ColumnModel c) => di.Quote(c.DbName);
        string big(string expression) => $"CAST({expression} AS {(di.Dialect == SqlDialect.MySql ? "SIGNED" : "BIGINT")})";
        string nothing = di.Dialect switch { SqlDialect.PostgreSql => "CAST(NULL AS TEXT)", SqlDialect.MySql => "CAST(NULL AS CHAR)", _ => "NULL" };
        string pick(ColumnModel? c) => c is null ? nothing : $"{product.Table}.{Q(c)}";
        string key = $"{product.Table}.{Q(s.Key)}", aProduct = $"{attributes.Table}.{Q(s.AttributeProduct)}", aName = $"{attributes.Table}.{Q(s.AttributeName)}", aValue = $"{attributes.Table}.{Q(s.AttributeValue)}";
        string distinct = $"COUNT(DISTINCT {aProduct})";
        string number = s.AttributeNumber is null ? "" : $"{attributes.Table}.{Q(s.AttributeNumber)}";
        string real = di.Dialect switch { SqlDialect.PostgreSql => "DOUBLE PRECISION", SqlDialect.MySql => "DOUBLE", _ => "REAL" };
        string rangeWhere = s.AttributeNumber is null ? "" : """
    for (name, low, high) in &query.ranges {
        if except.is_some_and(|skipped| skipped.to_lowercase() == name.to_lowercase()) {
            continue;
        }
        builder.push(§SQL_RANGE_OPEN).push_bind(name.clone()).push(§SQL_RANGE_MID);
        if let Some(low) = low {
            builder.push(§SQL_RANGE_LOW).push_bind(*low);
        }
        if let Some(high) = high {
            builder.push(§SQL_RANGE_HIGH).push_bind(*high);
        }
        builder.push(")");
    }
""";
        string bounds = s.AttributeNumber is null ? "let (low, high): (Option<f64>, Option<f64>) = (None, None);" : """
        let mut limits = sqlx::QueryBuilder::<Db>::new(§SQL_BOUNDS_OPEN);
        limits.push_bind(name.clone()).push(§SQL_BOUNDS_MID);
        catalog_where(&mut limits, &query, Some(name));
        limits.push(")");
        let (low, high) = limits.build_query_as::<(Option<f64>, Option<f64>)>().fetch_one(&pool).await?;
""";
        string columns = string.Join(", ", s.SearchColumns.Select(c => RustNames.Str($"{product.Table}.{Q(c)}")));
        var inner = new (string, string)[]
        {
            ("§SQL_RANGE_OPEN", RustTable.Raw($" AND {key} IN (SELECT {aProduct} FROM {attributes.Table} WHERE {aName} = ")),
            ("§SQL_RANGE_MID", RustTable.Raw($" AND {number} IS NOT NULL")), ("§SQL_RANGE_LOW", RustTable.Raw($" AND {number} >= ")), ("§SQL_RANGE_HIGH", RustTable.Raw($" AND {number} <= ")),
            ("§SQL_BOUNDS_OPEN", RustTable.Raw($"SELECT CAST(MIN({number}) AS {real}), CAST(MAX({number}) AS {real}) FROM {attributes.Table} WHERE {aName} = ")),
            ("§SQL_BOUNDS_MID", RustTable.Raw($" AND {number} IS NOT NULL AND {aProduct} IN (SELECT {key} FROM {product.Table} WHERE 1 = 1")),
        };
        rangeWhere = TemplateText.Fill(rangeWhere, inner);
        bounds = TemplateText.Fill(bounds, inner);
        return TemplateText.FillAll(CodeText, [
            ("§COLUMNS", columns), ("§KT", product.Key.Type), ("§RANGEWHERE", rangeWhere), ("§BOUNDS", bounds),
            ("§SQL_VALUE_LIKE", RustTable.Raw($" AND ({key} IN (SELECT {aProduct} FROM {attributes.Table} WHERE LOWER({aValue}) LIKE ")),
            ("§SQL_RANGE_OPEN", RustTable.Raw($" AND {key} IN (SELECT {aProduct} FROM {attributes.Table} WHERE {aName} = ")),
            ("§SQL_RANGE_MID", RustTable.Raw($" AND {number} IS NOT NULL")), ("§SQL_RANGE_LOW", RustTable.Raw($" AND {number} >= ")), ("§SQL_RANGE_HIGH", RustTable.Raw($" AND {number} <= ")),
            ("§SQL_BOUNDS_OPEN", RustTable.Raw($"SELECT CAST(MIN({number}) AS {real}), CAST(MAX({number}) AS {real}) FROM {attributes.Table} WHERE {aName} = ")),
            ("§SQL_BOUNDS_MID", RustTable.Raw($" AND {number} IS NOT NULL AND {aProduct} IN (SELECT {key} FROM {product.Table} WHERE 1 = 1")),
            ("§SQL_ATTR_OPEN", RustTable.Raw($"SELECT {aName}, {big(distinct)} FROM {attributes.Table} WHERE {aProduct} IN (SELECT {key} FROM {product.Table} WHERE 1 = 1")),
            ("§SQL_ATTR_CLOSE", RustTable.Raw($") GROUP BY {aName} ORDER BY {distinct} DESC, {aName} LIMIT ")),
            ("§SQL_VALUE_OPEN", RustTable.Raw($"SELECT {aValue}, {big(distinct)} FROM {attributes.Table} WHERE {aName} = ")),
            ("§SQL_VALUE_MID", RustTable.Raw($" AND {aValue} IS NOT NULL AND {aProduct} IN (SELECT {key} FROM {product.Table} WHERE 1 = 1")),
            ("§SQL_VALUE_CLOSE", RustTable.Raw($") GROUP BY {aValue} ORDER BY {distinct} DESC, {aValue} LIMIT ")),
            ("§SQL_COUNT", RustTable.Raw($"SELECT {big("COUNT(*)")} FROM {product.Table} WHERE 1 = 1")),
            ("§SQL_PAGE", RustTable.Raw($"SELECT {key}, {pick(s.Title)}, {pick(s.Code)}, {pick(s.Detail)} FROM {product.Table} WHERE 1 = 1")),
            ("§SQL_ORDER", RustTable.Raw($" ORDER BY {key} LIMIT ")),
            ("§SQL_FILTER_OPEN", RustTable.Raw($" AND {key} IN (SELECT {aProduct} FROM {attributes.Table} WHERE {aName} = ")),
            ("§SQL_FILTER_VALUES", RustTable.Raw($" AND {aValue} IN (")),
            ("§MAXFACETS", CatalogPlan.MaxFacets.ToString()), ("§MAXVALUES", CatalogPlan.MaxValues.ToString()), ("§PAGESIZE", CatalogPlan.DefaultPageSize), ("§MAXPAGE", CatalogPlan.MaxPageSize.ToString()),
            ("§BADFILTERTITLE", CatalogWords.BadFilterTitle), ("§BADFILTERTYPE", CatalogWords.BadFilterType), ("§W_BADFILTER", CatalogWords.BadFilter),
            ("§BADPAGETITLE", CatalogWords.BadPageTitle), ("§BADPAGETYPE", CatalogWords.BadPageType), ("§W_BADPAGE", CatalogWords.BadPage),
        ]);
    }

    private const string CodeText = """"
struct CatalogQuery {
    text: String,
    filters: Vec<(String, Vec<String>)>,
    page: Option<i64>,
    page_size: Option<i64>,
    max_facets: Option<i64>,
    max_values: Option<i64>,
    ranges: Vec<(String, Option<f64>, Option<f64>)>,
}

fn catalog_bad_filter() -> ApiError {
    ApiError::Rejected(400, "§BADFILTERTYPE", "§BADFILTERTITLE", "§W_BADFILTER".into())
}

fn catalog_bad_page() -> ApiError {
    ApiError::Rejected(400, "§BADPAGETYPE", "§BADPAGETITLE", "§W_BADPAGE".into())
}

/// The keyword (lower case), the chosen values by attribute name (each is written Name:Value) and the paging of a request.
fn read_catalog(pairs: Vec<(String, String)>) -> ApiResult<CatalogQuery> {
    let mut query = CatalogQuery { text: String::new(), filters: Vec::new(), page: None, page_size: None, max_facets: None, max_values: None, ranges: Vec::new() };
    for (key, value) in pairs {
        match key.as_str() {
            "q" => query.text = value.trim().to_lowercase(),
            "filter" => {
                let (name, chosen) = match value.split_once(':') {
                    Some((name, chosen)) => (name.trim().to_string(), chosen.trim().to_string()),
                    None => (String::new(), String::new()),
                };
                if name.is_empty() || chosen.is_empty() {
                    return Err(catalog_bad_filter());
                }
                match query.filters.iter_mut().find(|(known, _)| known.to_lowercase() == name.to_lowercase()) {
                    Some((_, values)) => {
                        if !values.contains(&chosen) {
                            values.push(chosen);
                        }
                    }
                    None => query.filters.push((name, vec![chosen])),
                }
            }
            "range" => {
                let mut parts = value.rsplitn(3, ':');
                let high = parts.next().unwrap_or("").trim().to_string();
                let low = parts.next().unwrap_or("").trim().to_string();
                let name = parts.next().unwrap_or("").trim().to_string();
                let number = |text: &str| -> ApiResult<Option<f64>> {
                    if text.is_empty() {
                        Ok(None)
                    } else {
                        text.parse::<f64>().ok().filter(|n| n.is_finite()).map(Some).ok_or_else(catalog_bad_filter)
                    }
                };
                if name.is_empty() || (low.is_empty() && high.is_empty()) {
                    return Err(catalog_bad_filter());
                }
                query.ranges.push((name, number(&low)?, number(&high)?));
            }
            "page" => query.page = Some(value.parse().map_err(|_| catalog_bad_page())?),
            "pageSize" => query.page_size = Some(value.parse().map_err(|_| catalog_bad_page())?),
            "maxFacets" => query.max_facets = value.parse().ok(),
            "maxValues" => query.max_values = value.parse().ok(),
            _ => {}
        }
    }
    Ok(query)
}

/// Writes the condition of the products that match the keyword (lower case on both sides) and every chosen attribute but the one named in `except`: several values of one attribute are alternatives, different attributes all have to match.
fn catalog_where(builder: &mut sqlx::QueryBuilder<Db>, query: &CatalogQuery, except: Option<&str>) {
    if !query.text.is_empty() {
        let like = format!("%{}%", query.text.replace('!', "!!").replace('%', "!%").replace('_', "!_"));
        builder.push(§SQL_VALUE_LIKE).push_bind(like.clone()).push(" ESCAPE '!')");
        let columns: &[&str] = &[§COLUMNS];
        for column in columns {
            builder.push(" OR LOWER(").push(*column).push(") LIKE ").push_bind(like.clone()).push(" ESCAPE '!'");
        }
        builder.push(")");
    }
    for (name, values) in &query.filters {
        if except.is_some_and(|skipped| skipped.to_lowercase() == name.to_lowercase()) {
            continue;
        }
        builder.push(§SQL_FILTER_OPEN).push_bind(name.clone()).push(§SQL_FILTER_VALUES);
        let mut list = builder.separated(", ");
        for value in values {
            list.push_bind(value.clone());
        }
        list.push_unseparated("))");
    }
§RANGEWHERE}

/// The attributes of the products that match the keyword and the filters (the most common first), each with its values and the number of products that carry them. An attribute is counted without its own filter.
async fn catalog_facets(State(pool): State<Pool>, Query(pairs): Query<Vec<(String, String)>>) -> ApiResult<Json<serde_json::Value>> {
    let query = read_catalog(pairs)?;
    let facet_limit = query.max_facets.unwrap_or(§MAXFACETS).clamp(1, 50);
    let value_limit = query.max_values.unwrap_or(§MAXVALUES).clamp(1, 100);
    let mut builder = sqlx::QueryBuilder::<Db>::new(§SQL_ATTR_OPEN);
    catalog_where(&mut builder, &query, None);
    builder.push(§SQL_ATTR_CLOSE).push_bind(facet_limit);
    let mut names: Vec<String> = builder.build_query_as::<(String, i64)>().fetch_all(&pool).await?.into_iter().map(|(name, _)| name).collect();
    for name in query.filters.iter().map(|(name, _)| name).chain(query.ranges.iter().map(|(name, _, _)| name)) {
        if !names.iter().any(|shown| shown.to_lowercase() == name.to_lowercase()) {
            names.push(name.clone());
        }
    }
    let mut groups: Vec<serde_json::Value> = Vec::new();
    for name in &names {
        let selected: Vec<String> = query.filters.iter().find(|(chosen, _)| chosen.to_lowercase() == name.to_lowercase()).map(|(_, values)| values.clone()).unwrap_or_default();
        let mut builder = sqlx::QueryBuilder::<Db>::new(§SQL_VALUE_OPEN);
        builder.push_bind(name.clone()).push(§SQL_VALUE_MID);
        catalog_where(&mut builder, &query, Some(name));
        builder.push(§SQL_VALUE_CLOSE).push_bind(value_limit);
        let counted = builder.build_query_as::<(String, i64)>().fetch_all(&pool).await?;
        let mut values: Vec<serde_json::Value> = counted.iter().map(|(value, count)| serde_json::json!({ "value": value, "count": count, "selected": selected.contains(value) })).collect();
        for value in &selected {
            if !counted.iter().any(|(shown, _)| shown == value) {
                values.push(serde_json::json!({ "value": value, "count": 0, "selected": true }));
            }
        }
        §BOUNDS
        let picked = query.ranges.iter().find(|(chosen, _, _)| chosen.to_lowercase() == name.to_lowercase());
        groups.push(serde_json::json!({ "name": name, "values": values, "min": low, "max": high, "selectedMin": picked.and_then(|p| p.1), "selectedMax": picked.and_then(|p| p.2) }));
    }
    let mut count = sqlx::QueryBuilder::<Db>::new(§SQL_COUNT);
    catalog_where(&mut count, &query, None);
    let total: i64 = count.build_query_scalar::<i64>().fetch_one(&pool).await?;
    Ok(Json(serde_json::json!({ "total": total, "facets": groups })))
}

/// The page of products that match the keyword and the filters, in key order.
async fn catalog_narrow(State(pool): State<Pool>, Query(pairs): Query<Vec<(String, String)>>) -> ApiResult<Json<serde_json::Value>> {
    let query = read_catalog(pairs)?;
    if query.page.is_some_and(|page| page < 1) || query.page_size.is_some_and(|size| size < 1) {
        return Err(catalog_bad_page());
    }
    let size = query.page_size.unwrap_or(§PAGESIZE).min(§MAXPAGE);
    let number = query.page.unwrap_or(1);
    let mut count = sqlx::QueryBuilder::<Db>::new(§SQL_COUNT);
    catalog_where(&mut count, &query, None);
    let total: i64 = count.build_query_scalar::<i64>().fetch_one(&pool).await?;
    let mut builder = sqlx::QueryBuilder::<Db>::new(§SQL_PAGE);
    catalog_where(&mut builder, &query, None);
    builder.push(§SQL_ORDER).push_bind(size).push(" OFFSET ").push_bind((number - 1).saturating_mul(size));
    let rows = builder.build_query_as::<(§KT, Option<String>, Option<String>, Option<String>)>().fetch_all(&pool).await?;
    let items: Vec<serde_json::Value> = rows.into_iter().map(|(id, title, code, detail)| serde_json::json!({ "id": id, "title": title, "code": code, "detail": detail })).collect();
    Ok(Json(serde_json::json!({ "total": total, "page": number, "pageSize": size, "items": items })))
}
"""";
}
