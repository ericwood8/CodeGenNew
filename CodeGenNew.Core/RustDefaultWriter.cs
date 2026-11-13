using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Rust (axum, sqlx) counterpart of the default-row part of <see cref="RowShapeWriter"/>: a table listed in <c>DefaultRowTables</c> keeps exactly one default row per group. A save settles the mark
/// before the row is written (<c>settle_default</c>) and, when the row asks for the mark while another row holds it, moves it in one transaction after the save (<c>move_default</c>); <c>POST {id}/make-default</c> moves it
/// the same way and a delete of the default row hands the mark to the next row first. Written into the table's own router (RS_Routes). </summary>
public static class RustDefaultWriter
{
    /// <summary> A group column: the struct field, the database column and whether the field is optional. </summary>
    public sealed record Group(string Field, string Column, bool IsOption);

    /// <summary> What the code needs of the table: struct, key and flag fields, the group columns and the table. </summary>
    public sealed record Names(string Struct, string KeyField, string KeyColumn, string KeyType, string FlagField, string FlagColumn, IReadOnlyList<Group> Groups, string Table);

    /// <summary> The route line to add to the router. </summary>
    public static string RouteLine(string route, string writeLayer) => $"        .route(\"{route}/{{id}}/make-default\", post(make_default){writeLayer})\n";

    public static string Code(Names n)
    {
        var groups = new StringBuilder();
        foreach (var g in n.Groups)
            groups.Append(g.IsOption
                ? $"    match &row.{g.Field} {{\n        Some(value) => {{\n            builder.push({RustTable.Raw($" AND {g.Column} = ")}).push_bind(value.clone());\n        }}\n        None => {{\n            builder.push({RustTable.Raw($" AND {g.Column} IS NULL")});\n        }}\n    }}\n"
                : $"    builder.push({RustTable.Raw($" AND {g.Column} = ")}).push_bind(row.{g.Field}.clone());\n");
        return TemplateText.FillAll(Text, ("§STRUCT", n.Struct), ("§KEYFIELD", n.KeyField), ("§KEYCOLUMN", n.KeyColumn), ("§KT", n.KeyType), ("§FLAGFIELD", n.FlagField), ("§FLAGCOLUMN", n.FlagColumn),
            ("§TABLE", n.Table), ("§GROUPS", groups.ToString()));
    }

    private const string Text = """"
/// Adds the test that a row is in the same group as `row`.
fn push_group(builder: &mut sqlx::QueryBuilder<Db>, row: &§STRUCT) {
§GROUPS}

/// Exactly one row of a group is the default: a row saved without the mark gets it when its group has none. A row that asks for the mark while another row holds it is saved without it and gets it from
/// `move_default` after the save (true is returned), so the group never has two marks and never has none.
async fn settle_default(pool: &Pool, row_id: Option<§KT>, row: &mut §STRUCT) -> ApiResult<bool> {
    let mut builder = sqlx::QueryBuilder::<Db>::new(r#"SELECT COUNT(*) FROM §TABLE WHERE §FLAGCOLUMN = "#);
    builder.push_bind(true);
    if let Some(id) = row_id {
        builder.push(r#" AND §KEYCOLUMN <> "#).push_bind(id);
    }
    push_group(&mut builder, row);
    let has_default = builder.build_query_scalar::<i64>().fetch_one(pool).await? > 0;
    if !row.§FLAGFIELD {
        row.§FLAGFIELD = !has_default;
        return Ok(false);
    }
    if has_default {
        row.§FLAGFIELD = false;
        return Ok(true);
    }
    Ok(false)
}

/// The row takes the mark from the others of its group, in one transaction.
async fn move_default(pool: &Pool, row: &§STRUCT) -> ApiResult<()> {
    let mut transaction = pool.begin().await?;
    let mut clear = sqlx::QueryBuilder::<Db>::new(r#"UPDATE §TABLE SET §FLAGCOLUMN = "#);
    clear.push_bind(false).push(r#" WHERE §FLAGCOLUMN = "#).push_bind(true).push(r#" AND §KEYCOLUMN <> "#).push_bind(row.§KEYFIELD.clone());
    push_group(&mut clear, row);
    clear.build().execute(&mut *transaction).await?;
    let mut mark = sqlx::QueryBuilder::<Db>::new(r#"UPDATE §TABLE SET §FLAGCOLUMN = "#);
    mark.push_bind(true).push(r#" WHERE §KEYCOLUMN = "#).push_bind(row.§KEYFIELD.clone());
    mark.build().execute(&mut *transaction).await?;
    transaction.commit().await?;
    Ok(())
}

/// Before the default row of a group is deleted, the mark moves to the next row of the group (the lowest key); the deleted row never keeps it.
async fn promote_default(pool: &Pool, id: §KT) -> ApiResult<()> {
    let Some(row) = repo::get_by_id(pool, id).await? else {
        return Ok(());
    };
    if !row.§FLAGFIELD {
        return Ok(());
    }
    let mut builder = sqlx::QueryBuilder::<Db>::new(r#"SELECT §KEYCOLUMN FROM §TABLE WHERE §KEYCOLUMN <> "#);
    builder.push_bind(id);
    push_group(&mut builder, &row);
    builder.push(r#" ORDER BY §KEYCOLUMN LIMIT 1"#);
    let next = builder.build_query_scalar::<§KT>().fetch_optional(pool).await?;
    let mut transaction = pool.begin().await?;
    let mut clear = sqlx::QueryBuilder::<Db>::new(r#"UPDATE §TABLE SET §FLAGCOLUMN = "#);
    clear.push_bind(false).push(r#" WHERE §KEYCOLUMN = "#).push_bind(id);
    clear.build().execute(&mut *transaction).await?;
    if let Some(next) = next {
        let mut mark = sqlx::QueryBuilder::<Db>::new(r#"UPDATE §TABLE SET §FLAGCOLUMN = "#);
        mark.push_bind(true).push(r#" WHERE §KEYCOLUMN = "#).push_bind(next);
        mark.build().execute(&mut *transaction).await?;
    }
    transaction.commit().await?;
    Ok(())
}

/// The row becomes the default of its group and the others lose the mark, in one transaction.
async fn make_default(State(pool): State<Pool>, Path(id): Path<§KT>) -> ApiResult<Json<§STRUCT>> {
    let row = repo::get_by_id(&pool, id).await?.ok_or(ApiError::NotFound)?;
    if !row.§FLAGFIELD {
        move_default(&pool, &row).await?;
    }
    repo::get_by_id(&pool, id).await?.map(Json).ok_or(ApiError::NotFound)
}

"""";
}
