using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Python (FastAPI) counterpart of the polymorphic-link part of <see cref="RowShapeWriter"/>: <c>app/routes/link_rules.py</c>, SQLAlchemy events on the link table's class (the record type is written in the
/// table's own spelling and the row must exist) and on every listed table's class (a delete is refused while a link names the row, or removes the links). The words are <see cref="LinkWords"/>. </summary>
public static class PythonLinkWriter
{
    public static IReadOnlyList<LinkShape> Links(DatabaseModel database, ProjectSettings project) =>
        database.Tables.Select(t => PolymorphicPlan.LinkOf(t, project, database)).OfType<LinkShape>().OrderBy(l => l.Table.TableName, StringComparer.OrdinalIgnoreCase).ToList();

    public static string File(IReadOnlyList<LinkShape> links, ProjectSettings project)
    {
        var imports = new SortedSet<string>(StringComparer.Ordinal);
        var body = new StringBuilder();
        string Import(TableModel table)
        {
            var main = PythonTable.Of(table, project);
            imports.Add($"from app.models.{main.Module} import {main.Class}");
            return main.Class;
        }

        foreach (var link in links)
        {
            var main = PythonTable.Of(link.Table, project);
            string row = Import(link.Table);
            string type = main.FieldOf(link.TypeColumn).Name, id = main.FieldOf(link.IdColumn).Name;
            var targets = new StringBuilder();
            foreach (var target in link.Targets)
            {
                string targetClass = Import(target.Table);
                targets.Append($"    \"{target.Table.TableName.ToLowerInvariant()}\": (\"{target.Table.TableName}\", {targetClass}, \"{PythonTable.Of(target.Table, project).FieldOf(target.Key).Name}\"),\n");
            }
            body.Append(TemplateText.FillAll(Check, new (string, string)[]
            {
                ("§LINK", PythonNames.Snake(link.Table.TableName)), ("§UPPER", PythonNames.Snake(link.Table.TableName).ToUpperInvariant()), ("§ROW", row), ("§TYPE", type), ("§ID", id), ("§TARGETS", targets.ToString().TrimEnd('\n')),
                ("§TYPELIST", PythonNames.Str(LinkWords.UnknownType(link.Targets.Select(t => t.Table.TableName)))), ("§UNKNOWN_TYPE", LinkWords.UnknownTypeType), ("§MISSING_TYPE", LinkWords.TargetMissingType)
            }));
        }

        // a listed table's delete: refused while a link names the row, or the links are removed
        foreach (var table in links.SelectMany(l => l.Targets).Select(t => t.Table).DistinctBy(t => t.TableName).OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
        {
            var linked = links.Where(l => l.Targets.Any(t => t.Table.TableName.EqualsIgnoreCase(table.TableName))).ToList();
            var main = PythonTable.Of(table, project);
            string targetClass = Import(table);
            string key = main.FieldOf(table.PrimaryKeyColumns[0]).Name;
            var refuse = new StringBuilder();
            var remove = new StringBuilder();
            foreach (var link in linked)
            {
                var linkMain = PythonTable.Of(link.Table, project);
                string linkClass = Import(link.Table);
                string type = linkMain.FieldOf(link.TypeColumn).Name, id = linkMain.FieldOf(link.IdColumn).Name;
                string where = $"{linkClass}.{type} == \"{table.TableName}\", {linkClass}.{id} == target.{key}";
                if (link.RemoveWithTarget)
                    remove.Append($"    connection.execute(delete({linkClass}).where({where}))\n");
                else
                    refuse.Append($"    if connection.execute(select(func.count()).select_from({linkClass}).where({where})).scalar():\n        raise ApiError(409, \"Still linked\", problem_type=\"{LinkWords.TargetInUseType}\", detail={PythonNames.Str(LinkWords.InUse(link.Table.TableName))})\n");
            }
            if (refuse.Length > 0)
                body.Append($"\n\n\n@event.listens_for({targetClass}, \"before_delete\")\ndef {PythonNames.Snake(table.TableName)}_not_while_linked(mapper, connection, target):\n{refuse.ToString().TrimEnd('\n')}\n");
            if (remove.Length > 0)
                body.Append($"\n\n\n@event.listens_for({targetClass}, \"after_delete\")\ndef {PythonNames.Snake(table.TableName)}_links_removed(mapper, connection, target):\n{remove.ToString().TrimEnd('\n')}\n");
        }

        return TemplateText.FillAll(Template, new (string, string)[] { ("§IMPORTS", string.Join("\n", imports)), ("§BODY", body.ToString().TrimEnd('\n')) }) + "\n";
    }

    private const string Template = """"
"""The polymorphic links (PolymorphicLinks): a row that attaches to a row of any of several tables. A save whose record type names no listed table, or whose id is no row of it, is refused (400) and the type is
written in the table's own spelling; the delete of a listed table's row is refused while links name it (409) or removes them. The same sentences and problem types as the ASP.NET API (RowShapes). Generated by PY_Links."""
from fastapi import APIRouter
from sqlalchemy import delete, event, func, select

§IMPORTS
from app.support import ApiError

router = APIRouter()   # no routes: the module is listed with the others so that importing it registers the events
§BODY
"""";

    private const string Check = """"


§UPPER_TARGETS = {
§TARGETS
}


def check_§LINK(mapper, connection, target):
    spec = §UPPER_TARGETS.get((target.§TYPE or "").strip().lower())
    if spec is None:
        raise ApiError(400, "Unknown record type", problem_type="§UNKNOWN_TYPE", detail=§TYPELIST)
    name, model, key = spec
    target.§TYPE = name
    if not connection.execute(select(func.count()).select_from(model).where(getattr(model, key) == target.§ID)).scalar():
        raise ApiError(400, "No such record", problem_type="§MISSING_TYPE", detail=f"There is no {name} with that id.")


event.listen(§ROW, "before_insert", check_§LINK)
event.listen(§ROW, "before_update", check_§LINK)
"""";
}
