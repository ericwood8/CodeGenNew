namespace CodeGenNew.Core;

/// <summary> The Rust (axum, sqlx) counterpart of the tree part of <see cref="RowShapeWriter"/>: the checks and routes of a table listed in <c>TreeTables</c>, written into the table's own router (RS_Routes). </summary>
public static class RustTreeWriter
{
    /// <summary> The routes to add to the router, after the table's own. </summary>
    public static string RouteLines(string route, string readLayer, string writeLayer, bool roles) =>
        $"        .route(\"{route}/tree\", get(tree){(roles ? readLayer : "")})\n        .route(\"{route}/{{id}}/promote-children\", post(promote_children){(roles ? writeLayer : "")})\n";

    /// <summary> The constants, <c>check_tree</c>, <c>refuse_children</c>, <c>tree</c> and <c>promote_children</c>. <paramref name="text"/> is the name column's field, or null. </summary>
    public static string Code(TreeTable tree, DialectInfo di, string keyType, bool hasText, string liveAnd = "", string liveWhere = "")
    {
        string table = di.QuoteTable(tree.Table.SchemaName, tree.Table.DbTableName);
        string key = di.Quote(tree.Key.DbName), parent = di.Quote(tree.Parent.DbName);
        string text = tree.Text is null ? "''" : di.Quote(tree.Text.DbName);
        string one = di.Placeholder(1), two = di.Placeholder(2);
        return Fill(Text,
            ("§PARENT_SQL", RustTable.Raw($"SELECT {parent} FROM {table} WHERE {key} = {one}{liveAnd}")),
            ("§COUNT_SQL", RustTable.Raw($"SELECT COUNT(*) FROM {table} WHERE {parent} = {one}{liveAnd}")),
            ("§PROMOTE_SQL", RustTable.Raw($"UPDATE {table} SET {parent} = {one} WHERE {parent} = {two}{liveAnd}")),
            ("§ROWS_SQL", RustTable.Raw($"SELECT {key}, {parent}, {text} FROM {table}{liveWhere}")),
            ("§CHILDREN_SQL", RustTable.Raw($"SELECT {key} FROM {table} WHERE {parent} IN ")),
            ("§LIVEPUSH", liveAnd.Length == 0 ? "" : $"            builder.push({RustTable.Raw(liveAnd)});\n"), ("§MAX", tree.MaxDepth.ToString()), ("§KT", keyType),
            ("§TEXTVALUE", hasText ? "text.unwrap_or_default()" : "{ let _ = text; id.to_string() }"),
            ("§CYCLE", RowShapeWords.TreeCycleType), ("§DEEP", RowShapeWords.TreeTooDeepType), ("§CHILDREN", RowShapeWords.TreeHasChildrenType),
            ("§W_OWN", RowShapeWords.OwnParent), ("§W_UNDER", RowShapeWords.UnderItself), ("§W_CHILDREN", RowShapeWords.HasChildren), ("§W_DEEP", RowShapeWords.TooDeep(tree.MaxDepth)));
    }

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string Text = """"
const TREE_MAX_DEPTH: i32 = §MAX;
const TREE_PARENT: &str = §PARENT_SQL;
const TREE_COUNT: &str = §COUNT_SQL;
const TREE_PROMOTE: &str = §PROMOTE_SQL;
const TREE_ROWS: &str = §ROWS_SQL;
const TREE_CHILDREN: &str = §CHILDREN_SQL;

/// A parent that is the row itself or one of its descendants would make a cycle, and a level past TREE_MAX_DEPTH (0: no limit) is too deep.
async fn check_tree(pool: &Pool, row_id: Option<§KT>, parent: Option<§KT>) -> ApiResult<()> {
    let Some(parent) = parent else { return Ok(()) };
    if Some(parent) == row_id {
        return Err(ApiError::Rejected(400, "§CYCLE", "Not a valid parent", "§W_OWN".into()));
    }
    let mut depth = 1;
    let mut seen: Vec<§KT> = row_id.into_iter().collect();
    let mut up = Some(parent);
    while let Some(id) = up {
        if seen.contains(&id) {
            return Err(ApiError::Rejected(400, "§CYCLE", "Not a valid parent", "§W_UNDER".into()));
        }
        seen.push(id);
        depth += 1;
        up = sqlx::query_scalar::<_, Option<§KT>>(TREE_PARENT).bind(id).fetch_optional(pool).await?.flatten();
    }
    if TREE_MAX_DEPTH > 0 {
        let mut below = 0;
        let mut level: Vec<§KT> = row_id.into_iter().collect();
        while !level.is_empty() && depth + below <= TREE_MAX_DEPTH {
            let mut builder = sqlx::QueryBuilder::<Db>::new(TREE_CHILDREN);
            builder.push("(");
            let mut separated = builder.separated(", ");
            for id in &level {
                separated.push_bind(*id);
            }
            builder.push(")");
§LIVEPUSH            level = builder.build_query_scalar::<§KT>().fetch_all(pool).await?;
            if level.is_empty() {
                break;
            }
            below += 1;
        }
        if depth + below > TREE_MAX_DEPTH {
            return Err(ApiError::Rejected(400, "§DEEP", "Too deep", "§W_DEEP".into()));
        }
    }
    Ok(())
}

/// A row with children is not deleted: they would be orphaned or refused by the foreign key.
async fn refuse_children(pool: &Pool, id: §KT) -> ApiResult<()> {
    if sqlx::query_scalar::<_, i64>(TREE_COUNT).bind(id).fetch_one(pool).await? > 0 {
        return Err(ApiError::Rejected(409, "§CHILDREN", "Has child rows", "§W_CHILDREN".into()));
    }
    Ok(())
}

#[derive(serde::Serialize)]
struct TreeNode {
    id: §KT,
    #[serde(rename = "parentId")]
    parent_id: Option<§KT>,
    depth: i32,
    #[serde(rename = "hasChildren")]
    has_children: bool,
    text: String,
}

/// Every row depth first (children after their parent, by name) with its level; only a row with no parent is a root.
async fn tree(State(pool): State<Pool>) -> ApiResult<Json<Vec<TreeNode>>> {
    let rows: Vec<(§KT, Option<§KT>, Option<String>)> = sqlx::query_as(TREE_ROWS).fetch_all(&pool).await?;
    let mut by_parent: std::collections::HashMap<Option<§KT>, Vec<(§KT, String)>> = std::collections::HashMap::new();
    for (id, parent, text) in rows {
        by_parent.entry(parent).or_default().push((id, §TEXTVALUE));
    }
    for list in by_parent.values_mut() {
        list.sort_by(|a, b| a.1.to_lowercase().cmp(&b.1.to_lowercase()).then(a.0.cmp(&b.0)));
    }
    let mut result = Vec::new();
    let mut seen = std::collections::HashSet::new();
    let mut stack: Vec<(§KT, Option<§KT>, String, i32)> = by_parent.get(&None).map(|list| list.iter().rev().map(|(id, text)| (*id, None, text.clone(), 1)).collect()).unwrap_or_default();
    while let Some((id, parent_id, text, depth)) = stack.pop() {
        if !seen.insert(id) {
            continue;
        }
        let children = by_parent.get(&Some(id));
        result.push(TreeNode { id, parent_id, depth, has_children: children.is_some_and(|list| !list.is_empty()), text });
        for (child, child_text) in children.into_iter().flatten().rev() {
            stack.push((*child, Some(id), child_text.clone(), depth + 1));
        }
    }
    Ok(Json(result))
}

#[derive(serde::Serialize)]
struct Moved {
    moved: u64,
}

/// The children of the row become children of the row's own parent (roots when it is a root); the row stays.
async fn promote_children(State(pool): State<Pool>, Path(id): Path<§KT>) -> ApiResult<Json<Moved>> {
    let parent = sqlx::query_scalar::<_, Option<§KT>>(TREE_PARENT).bind(id).fetch_optional(&pool).await?.ok_or(ApiError::NotFound)?;
    let moved = sqlx::query(TREE_PROMOTE).bind(parent).bind(id).execute(&pool).await?.rows_affected();
    Ok(Json(Moved { moved }))
}

"""";
}
