using System.Text;

namespace CodeGenNew.Core;

/// <summary> The SQLAlchemy 2.0 model file of a table (<c>app/models/&lt;table&gt;.py</c>, starting with its <c>@@@FILE</c> marker). PY_Model writes it for every table with a CRUD API; PY_Approvals writes it for the
/// side tables of RowApprovals, which have no API of their own but are read and written by the approval routes. </summary>
public static class PythonModelFile
{
    public static string Text(TableModel model, ProjectSettings project)
    {
        var t = PythonTable.Of(model, project);
        var sb = new StringBuilder();
        void Line(string text = "") => sb.Append(text).Append('\n');
        string Str(string text) => PythonNames.Str(text);

        bool schemaNamed = model.Dialect is SqlDialect.SqlServer or SqlDialect.PostgreSql
            && !model.SchemaName.EqualsIgnoreCase(model.Dialect == SqlDialect.SqlServer ? "dbo" : "public");

        var saTypes = t.Fields.Select(f => System.Text.RegularExpressions.Regex.Match(f.SqlAlchemyType, "^[A-Za-z]+").Value).Where(n => n is not ("PgEnum" or "PgText" or "MySqlSpatial" or "MySqlBit" or "YEAR")).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        bool anyServerGenerated = t.Fields.Any(f => f.ReadOnly);
        if (anyServerGenerated)
            saTypes.Add("FetchedValue");
        var annotations = t.Fields.Select(f => f.Annotation).Distinct().ToList();

        Line($"@@@FILE app/models/{t.Module}.py@@@");
        if (annotations.Contains("Decimal"))
            Line("from decimal import Decimal");
        var datetimes = new[] { "date", "datetime", "time" }.Where(annotations.Contains).ToList();
        if (datetimes.Count > 0)
            Line($"from datetime import {string.Join(", ", datetimes)}");
        if (annotations.Contains("UUID"))
            Line("from uuid import UUID");
        Line();
        Line($"from sqlalchemy import {string.Join(", ", saTypes.OrderBy(n => n, StringComparer.Ordinal))}");
        if (t.Fields.Any(f => f.SqlAlchemyType == "YEAR"))
            Line("from sqlalchemy.dialects.mysql import YEAR");
        if (t.Fields.Any(f => f.SqlAlchemyType.StartsWith("PgEnum")))
            Line("from sqlalchemy.dialects.postgresql import ENUM as PgEnum");
        Line("from sqlalchemy.orm import Mapped, mapped_column");
        Line();
        var dbTypes = new[] { "MySqlBit", "MySqlSpatial", "PgText" }.Where(n => t.Fields.Any(f => f.SqlAlchemyType.StartsWith(n))).ToList();
        Line("from app.db import " + string.Join(", ", new[] { "Base" }.Concat(dbTypes)));
        Line();
        Line();
        Line($"class {t.Class}(Base):");
        Line($"    __tablename__ = {Str(model.DbTableName)}");
        if (schemaNamed)
            Line($"    __table_args__ = {{\"schema\": {Str(model.SchemaName)}}}");
        Line();
        foreach (var f in t.Fields)
        {
            var c = f.Column;
            var options = new List<string>();
            if (c.IsPrimaryKey)
            {
                options.Add("primary_key=True");
                options.Add(c.IsIdentity ? "autoincrement=True" : "autoincrement=False");
            }
            else
                options.Add(c.IsNullable ? "nullable=True" : "nullable=False");
            if (f.ReadOnly)
                options.Add("server_default=FetchedValue(), server_onupdate=FetchedValue()");
            string type = f.Annotation + (c.IsNullable ? " | None" : "");
            Line($"    {f.Name}: Mapped[{type}] = mapped_column({Str(c.DbName)}, {f.SqlAlchemyType}, {string.Join(", ", options)})");
        }
        if (t.Skipped.Count > 0)
        {
            Line();
            Line($"    # Left out (no Python mapping here): {string.Join(", ", t.Skipped.Select(c => c.Name))}.");
        }
        return sb.ToString();
    }
}
