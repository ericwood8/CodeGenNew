using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Rust (axum, sqlx) counterpart of the content part of <see cref="RowShapeWriter"/>: the slug and status rules, the publish, unpublish and archive routes and the two public reads of a table listed in
/// <c>ContentTables</c>, written into the table's own router (RS_Routes). The words are <see cref="ContentWords"/> and <see cref="ContentApiWords"/>. </summary>
public static class RustContentWriter
{
    /// <summary> The routes of the state changes, added to the router after the table's own. </summary>
    public static string StatusRouteLines(string route, string writeLayer) =>
        $"        .route(\"{route}/{{id}}/publish\", post(publish){writeLayer})\n        .route(\"{route}/{{id}}/unpublish\", post(unpublish){writeLayer})\n        .route(\"{route}/{{id}}/archive\", post(archive){writeLayer})\n";

    /// <summary> The two public reads, added to the router (no sign-in) or put in <c>public_router</c> (the sign-in check leaves it out). </summary>
    public static string PublicRouteLines(string route) =>
        $"        .route(\"{route}/public\", get(public_list))\n        .route(\"{route}/public/{{slug}}\", get(public_by_slug))\n";

    /// <summary> <c>make_slug</c>, the slug and status rules the create and update call, and the routes' handlers. <paramref name="scoped"/> says the table is limited to an owner (the state changes then check it), and
    /// <paramref name="userArgument"/> is the extractor of the caller. </summary>
    public static string Code(RustTable t, ContentShape content, DialectInfo di, string liveAnd, bool scoped, string userArgument)
    {
        string table = t.Table;
        var key = t.FieldOf(content.Key);
        var title = t.FieldOf(content.Title);
        var slug = t.FieldOf(content.Slug);
        var status = t.FieldOf(content.Status);
        var published = content.PublishedOn is null ? null : t.FieldOf(content.PublishedOn);
        string keyColumn = di.Quote(content.Key.DbName), slugColumn = di.Quote(content.Slug.DbName), statusColumn = di.Quote(content.Status.DbName);
        string publishedColumn = content.PublishedOn is null ? "" : di.Quote(content.PublishedOn.DbName);
        string live = RustTable.Raw(liveAnd);
        string order = published is null ? $"{keyColumn} DESC" : $"{publishedColumn} DESC, {keyColumn} DESC";
        string setStatus = $"UPDATE {table} SET {statusColumn} = {di.Placeholder(1)} WHERE {keyColumn} = {di.Placeholder(2)}{liveAnd}";
        string publish = published is null ? "" : $"UPDATE {table} SET {statusColumn} = {di.Placeholder(1)}, {publishedColumn} = COALESCE({publishedColumn}, {di.Placeholder(2)}) WHERE {keyColumn} = {di.Placeholder(3)}{liveAnd}";
        string publishConst = published is null ? "" : $"const CONTENT_PUBLISH: &str = {RustTable.Raw(publish)};\n";
        string now = published is not null && published.Type.Contains("DateTime<Utc>") ? "chrono::Utc::now()" : "chrono::Local::now().naive_local()";
        string changeStatus = published is null
            ? "    sqlx::query(CONTENT_SET_STATUS).bind(status).bind(id).execute(pool).await?;\n"
            : $"    if status == \"{ContentWords.Published}\" {{\n        sqlx::query(CONTENT_PUBLISH).bind(status).bind({now}).bind(id).execute(pool).await?;\n    }} else {{\n        sqlx::query(CONTENT_SET_STATUS).bind(status).bind(id).execute(pool).await?;\n    }}\n";
        string titleValue = title.IsOption ? $"row.{title.Name}.as_deref().unwrap_or_default()" : $"row.{title.Name}.as_str()";
        string scopeCheck = scoped ? "    let current = repo::get_by_id(&pool, id).await?.ok_or(ApiError::NotFound)?;\n    allowed(&pool, &user, &current).await?;\n" : "";
        string retain = content.PublicColumns is null ? "" : "    if let Some(object) = value.as_object_mut() {\n        object.retain(|name, _| CONTENT_PUBLIC.contains(&name.as_str()));\n    }\n";
        string publicConst = content.PublicColumns is null ? "" : $"const CONTENT_PUBLIC: &[&str] = &[{string.Join(", ", content.PublicColumns.Select(c => "\"" + t.FieldOf(c).Json + "\""))}];\n";
        string publicStatus = $"'{ContentWords.Published}'";
        return TemplateText.FillAll(Text,
            ("§STRUCT", t.Struct), ("§KT", t.Key.Type), ("§KEYFIELD", key.Name), ("§SLUGFIELD", slug.Name), ("§STATUSFIELD", status.Name), ("§SLUGMAX", content.SlugLength.ToString()),
            ("§SLUGCOUNT", RustTable.Raw($"SELECT COUNT(*) FROM {table} WHERE {slugColumn} = ")), ("§KEYNOT", RustTable.Raw($" AND {keyColumn} <> ")), ("§LIVE", live),
            ("§SETSTATUS", RustTable.Raw(setStatus)), ("§PUBLISHCONST", publishConst), ("§CHANGESTATUS", changeStatus), ("§TITLEVALUE", titleValue),
            ("§RESTOREPUBLISHED", published is null ? "" : $"    row.{published.Name} = stored.{published.Name}.clone();\n"), ("§CLEARPUBLISHED", published is null ? "" : $"    row.{published.Name} = None;\n"),
            ("§PUBLICLIST", RustTable.Raw($"SELECT {t.SelectList} FROM {table} WHERE {statusColumn} = {publicStatus}{liveAnd} ORDER BY {order} LIMIT {di.Placeholder(1)} OFFSET {di.Placeholder(2)}")),
            ("§PUBLICONE", RustTable.Raw($"SELECT {t.SelectList} FROM {table} WHERE {slugColumn} = {di.Placeholder(1)} AND {statusColumn} = {publicStatus}{liveAnd}")),
            ("§PUBLICCONST", publicConst), ("§RETAIN", retain), ("§USERARG", scoped ? userArgument : ""), ("§SCOPECHECK", scopeCheck),
            ("§DRAFT", ContentWords.Draft), ("§PUBLISHED", ContentWords.Published), ("§ARCHIVED", ContentWords.Archived), ("§FALLBACK", ContentWords.FallbackSlug),
            ("§TAKENTYPE", ContentWords.SlugTakenType), ("§TAKENTITLE", ContentApiWords.SlugTakenTitle), ("§W_TAKEN", ContentWords.SlugTaken),
            ("§PAGE", ContentApiWords.PublicPageSize.ToString()), ("§MAXPAGE", ContentApiWords.PublicMaxPageSize.ToString()));
    }

    private const string Text = """"
const CONTENT_SLUG_MAX: usize = §SLUGMAX;
const CONTENT_SLUG_COUNT: &str = §SLUGCOUNT;
const CONTENT_KEY_NOT: &str = §KEYNOT;
const CONTENT_LIVE: &str = §LIVE;
const CONTENT_SET_STATUS: &str = §SETSTATUS;
§PUBLISHCONST§PUBLICCONST
/// A slug is lower case ASCII letters and digits with single hyphens, at most max_length characters; text with nothing to make one from gives "§FALLBACK".
fn make_slug(text: &str, max_length: usize) -> String {
    use unicode_normalization::UnicodeNormalization;
    let mut slug = String::new();
    let mut hyphen = false;
    for c in text.nfd() {
        if unicode_normalization::char::is_combining_mark(c) {
            continue;
        }
        if c.is_ascii_alphanumeric() {
            slug.push(c.to_ascii_lowercase());
            hyphen = false;
        } else if !hyphen && !slug.is_empty() {
            slug.push('-');
            hyphen = true;
        }
    }
    let mut made = slug.trim_matches('-').to_string();
    if made.len() > max_length {
        made = made[..max_length].trim_matches('-').to_string();
    }
    if made.is_empty() { "§FALLBACK".to_string() } else { made }
}

/// The slug to store: made from the title when none is named, numbered (-2, -3) when a made slug is taken and refused (409) when a slug the caller named is taken.
async fn content_slug(pool: &Pool, row_id: Option<§KT>, named: Option<String>, title: &str) -> ApiResult<String> {
    let wanted = named.clone().unwrap_or_else(|| make_slug(title, CONTENT_SLUG_MAX));
    let mut candidate = wanted.clone();
    let mut number = 2;
    loop {
        let mut builder = sqlx::QueryBuilder::<Db>::new(CONTENT_SLUG_COUNT);
        builder.push_bind(candidate.clone());
        if let Some(id) = row_id {
            builder.push(CONTENT_KEY_NOT).push_bind(id);
        }
        builder.push(CONTENT_LIVE);
        if builder.build_query_scalar::<i64>().fetch_one(pool).await? == 0 {
            return Ok(candidate);
        }
        if named.is_some() {
            return Err(ApiError::Rejected(409, "§TAKENTYPE", "§TAKENTITLE", "§W_TAKEN".into()));
        }
        let suffix = format!("-{number}");
        let base = if wanted.len() + suffix.len() > CONTENT_SLUG_MAX { wanted[..CONTENT_SLUG_MAX - suffix.len()].trim_end_matches('-').to_string() } else { wanted.clone() };
        candidate = format!("{base}{suffix}");
        number += 1;
    }
}

/// A new row is a draft whatever it says, and its slug is made from the title when it is blank.
async fn new_content(pool: &Pool, row: &mut §STRUCT) -> ApiResult<()> {
    row.§STATUSFIELD = "§DRAFT".to_string();
§CLEARPUBLISHED    let named = if row.§SLUGFIELD.trim().is_empty() { None } else { Some(make_slug(&row.§SLUGFIELD, CONTENT_SLUG_MAX)) };
    let slug = content_slug(pool, None, named, §TITLEVALUE).await?;
    row.§SLUGFIELD = slug;
    Ok(())
}

/// A save never changes the status. The slug stays when it is blank or unchanged (a new title does not move the page); a different slug is a rename on purpose and is checked like a new one.
async fn change_content(pool: &Pool, id: §KT, row: &mut §STRUCT, stored: &§STRUCT) -> ApiResult<()> {
    row.§STATUSFIELD = stored.§STATUSFIELD.clone();
§RESTOREPUBLISHED    let named = if row.§SLUGFIELD.trim().is_empty() { None } else { Some(make_slug(&row.§SLUGFIELD, CONTENT_SLUG_MAX)) };
    if named.as_deref().is_none_or(|slug| slug == stored.§SLUGFIELD) {
        row.§SLUGFIELD = stored.§SLUGFIELD.clone();
        return Ok(());
    }
    let slug = content_slug(pool, Some(id), named, "").await?;
    row.§SLUGFIELD = slug;
    Ok(())
}

/// Publish (the first time also stamps the published-on time), unpublish (back to a draft) and archive; the row comes back, 404 when there is none. Doing it again changes nothing.
async fn change_status(pool: &Pool, id: §KT, status: &str) -> ApiResult<Json<§STRUCT>> {
§CHANGESTATUS    repo::get_by_id(pool, id).await?.map(Json).ok_or(ApiError::NotFound)
}

async fn publish(State(pool): State<Pool>, Path(id): Path<§KT>§USERARG) -> ApiResult<Json<§STRUCT>> {
§SCOPECHECK    change_status(&pool, id, "§PUBLISHED").await
}

async fn unpublish(State(pool): State<Pool>, Path(id): Path<§KT>§USERARG) -> ApiResult<Json<§STRUCT>> {
§SCOPECHECK    change_status(&pool, id, "§DRAFT").await
}

async fn archive(State(pool): State<Pool>, Path(id): Path<§KT>§USERARG) -> ApiResult<Json<§STRUCT>> {
§SCOPECHECK    change_status(&pool, id, "§ARCHIVED").await
}

/// What a reader who is not signed in sees of a row: the columns of ContentPublicColumns, or all of them.
fn public_view(row: &§STRUCT) -> serde_json::Value {
    let mut value = serde_json::to_value(row).unwrap_or_default();
§RETAIN    value
}

#[derive(serde::Deserialize)]
struct PublicQuery {
    #[serde(rename = "pageNumber")]
    page_number: Option<i64>,
    #[serde(rename = "pageSize")]
    page_size: Option<i64>,
}

const CONTENT_PUBLIC_LIST: &str = §PUBLICLIST;
const CONTENT_PUBLIC_ONE: &str = §PUBLICONE;

/// The published rows, the newest first, a page at a time (pageSize at most §MAXPAGE), for a reader who is not signed in.
async fn public_list(State(pool): State<Pool>, Query(query): Query<PublicQuery>) -> ApiResult<Json<Vec<serde_json::Value>>> {
    let size = query.page_size.unwrap_or(§PAGE).clamp(1, §MAXPAGE);
    let skip = (query.page_number.unwrap_or(1).max(1) - 1) * size;
    let rows = sqlx::query_as::<_, §STRUCT>(CONTENT_PUBLIC_LIST).bind(size).bind(skip).fetch_all(&pool).await?;
    Ok(Json(rows.iter().map(public_view).collect()))
}

/// One published row by its slug; a draft or an archived row is a 404, as if it were not there.
async fn public_by_slug(State(pool): State<Pool>, Path(slug): Path<String>) -> ApiResult<Json<serde_json::Value>> {
    let row = sqlx::query_as::<_, §STRUCT>(CONTENT_PUBLIC_ONE).bind(slug).fetch_optional(&pool).await?.ok_or(ApiError::NotFound)?;
    Ok(Json(public_view(&row)))
}

"""";
}
