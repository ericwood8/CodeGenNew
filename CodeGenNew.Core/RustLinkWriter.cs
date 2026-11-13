using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Rust (axum) counterpart of the polymorphic-link part of <see cref="RowShapeWriter"/>: <c>src/routes/link_rules.rs</c>, a check function for each link table (the record type is written in the table's own
/// spelling and the row must exist) and, for each listed table, a function that refuses its delete while a link names the row or removes the links afterwards. The routes of <c>RS_Routes</c> call them. The words are
/// <see cref="LinkWords"/>. </summary>
public static class RustLinkWriter
{
    public static IReadOnlyList<LinkShape> Links(DatabaseModel database, ProjectSettings project) =>
        database.Tables.Select(t => PolymorphicPlan.LinkOf(t, project, database)).OfType<LinkShape>().OrderBy(l => l.Table.TableName, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary> The line a link table's create and update run before the row is saved (four spaces in, no line end). </summary>
    public static string CheckLine(RustTable t) => $"    crate::routes::link_rules::check_{t.Module}(&pool, &mut row).await?;";

    public static string File(IReadOnlyList<LinkShape> links, ProjectSettings project, SqlDialect dialect)
    {
        var di = DialectInfo.For(dialect);
        string Str(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        string Q(ColumnModel c) => di.Quote(c.DbName);
        var o = new StringBuilder();
        var imports = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var link in links)
        {
            var t = RustTable.Of(link.Table, project);
            imports.Add($"use crate::models::{t.Module}::{t.Struct};");
            var type = t.FieldOf(link.TypeColumn);
            var id = t.FieldOf(link.IdColumn);
            string kind = type.IsOption ? $"row.{type.Name}.as_deref().unwrap_or(\"\")" : $"row.{type.Name}.as_str()";
            string assign = type.IsOption ? $"row.{type.Name} = Some(name.to_string());" : $"row.{type.Name} = name.to_string();";
            var arms = new StringBuilder();
            foreach (var target in link.Targets)
            {
                string sql = $"SELECT COUNT(*) FROM {di.QuoteTable(target.Table.SchemaName, target.Table.DbTableName)} WHERE {Q(target.Key)} = {di.Placeholder(1)}";
                arms.Append($"        {Str(target.Table.TableName.ToLowerInvariant())} => ({Str(target.Table.TableName)}, {RustTable.Raw(sql)}),\n");
            }
            o.Append(TemplateText.FillAll(Check, new (string, string)[]
            {
                ("§MODULE", t.Module), ("§STRUCT", t.Struct), ("§KIND", kind), ("§ASSIGN", assign), ("§ID", $"row.{id.Name}"), ("§ARMS", arms.ToString().TrimEnd('\n')),
                ("§TYPELIST", Str(LinkWords.UnknownType(link.Targets.Select(x => x.Table.TableName)))), ("§UNKNOWN_TYPE", LinkWords.UnknownTypeType), ("§MISSING_TYPE", LinkWords.TargetMissingType)
            }));
        }

        foreach (var table in links.SelectMany(l => l.Targets).Select(x => x.Table).DistinctBy(x => x.TableName).OrderBy(x => x.TableName, StringComparer.OrdinalIgnoreCase))
        {
            var main = RustTable.Of(table, project);
            string keyType = main.Key.Type;
            var refuse = new StringBuilder();
            var remove = new StringBuilder();
            foreach (var link in links.Where(l => l.Targets.Any(x => x.Table.TableName.EqualsIgnoreCase(table.TableName))))
            {
                string linkTable = di.QuoteTable(link.Table.SchemaName, link.Table.DbTableName);
                string where = $"WHERE {Q(link.TypeColumn)} = {di.Placeholder(1)} AND {Q(link.IdColumn)} = {di.Placeholder(2)}";
                if (link.RemoveWithTarget)
                    remove.Append($"    sqlx::query({RustTable.Raw($"DELETE FROM {linkTable} {where}")}).bind({Str(table.TableName)}).bind(id).execute(pool).await?;\n");
                else
                    refuse.Append($"    if sqlx::query_scalar::<_, i64>({RustTable.Raw($"SELECT COUNT(*) FROM {linkTable} {where}")}).bind({Str(table.TableName)}).bind(id).fetch_one(pool).await? > 0 {{\n" +
                        $"        return Err(ApiError::Rejected(409, \"{LinkWords.TargetInUseType}\", \"Still linked\", {Str(LinkWords.InUse(link.Table.TableName))}.into()));\n    }}\n");
            }
            if (refuse.Length > 0)
                o.Append($"/// A row that links still name is not deleted (409).\npub async fn refuse_linked_{main.Module}(pool: &Pool, id: {keyType}) -> ApiResult<()> {{\n{refuse}    Ok(())\n}}\n\n");
            if (remove.Length > 0)
                o.Append($"/// Once the row is gone, the links that named it are removed.\npub async fn remove_links_{main.Module}(pool: &Pool, id: {keyType}) -> ApiResult<()> {{\n{remove}    Ok(())\n}}\n\n");
        }

        return TemplateText.FillAll(Template, new (string, string)[] { ("§IMPORTS", string.Join("\n", imports)), ("§BODY", o.ToString().TrimEnd('\n')) }) + "\n";
    }

    /// <summary> Whether the delete of <paramref name="table"/> asks <c>refuse_linked_*</c> first. </summary>
    public static bool Refuses(TableModel table, ProjectSettings project) => PolymorphicPlan.RefusesDeleteOf(table.TableName, project);

    /// <summary> Whether the delete of <paramref name="table"/> runs <c>remove_links_*</c> afterwards. </summary>
    public static bool Removes(TableModel table, ProjectSettings project) => PolymorphicPlan.RemovesLinksOf(table.TableName, project);

    private const string Template = """"
// The polymorphic links (PolymorphicLinks): a row that attaches to a row of any of several tables. A save whose record type names no listed table, or whose id is no row of it, is refused (400) and the type is
// written in the table's own spelling; the delete of a listed table's row is refused while links name it (409) or removes them. The same sentences and problem types as the ASP.NET API (RowShapes). Generated by RS_Links.
use axum::Router;

§IMPORTS
use crate::support::{ApiError, ApiResult, Pool};

/// No routes: the module is listed with the others so that the table routes can call it.
pub fn router() -> Router<Pool> {
    Router::new()
}

§BODY
"""";

    private const string Check = """"
/// The record type must name one of the listed tables (it is written in the table's own spelling) and the id must be a row of it.
pub async fn check_§MODULE(pool: &Pool, row: &mut §STRUCT) -> ApiResult<()> {
    let (name, count_sql) = match §KIND.trim().to_lowercase().as_str() {
§ARMS
        _ => return Err(ApiError::Rejected(400, "§UNKNOWN_TYPE", "Unknown record type", §TYPELIST.into())),
    };
    §ASSIGN
    if sqlx::query_scalar::<_, i64>(count_sql).bind(§ID).fetch_one(pool).await? == 0 {
        return Err(ApiError::Rejected(400, "§MISSING_TYPE", "No such record", format!("There is no {name} with that id.")));
    }
    Ok(())
}

"""";
}
