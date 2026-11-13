using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Python (FastAPI) counterpart of the snapshot part of <see cref="RowShapeWriter"/>: a table listed in <c>SnapshotColumns</c> copies columns of its parents when a row is created, keeps them when it is changed
/// and works its line total out on every save. <c>fill_snapshot</c> is written into the table's own router and called before the commit. </summary>
public static class PythonSnapshotWriter
{
    /// <summary> One copied column as Python attributes. </summary>
    public sealed record Copy(string Target, string Source);

    /// <summary> The copies that come from one parent: the link attribute, whether it may be empty, the parent's class and module, its key attribute and the table's name for the sentence. </summary>
    public sealed record Parent(string Link, bool LinkIsNullable, string Class, string Module, string Key, string Table, IReadOnlyList<Copy> Copies);

    public sealed record Total(string Target, string FactorA, string FactorB);

    public sealed record Names(IReadOnlyList<Parent> Parents, Total? Total)
    {
        /// <summary> The attributes the server owns, which an update must not take from the body. </summary>
        public IEnumerable<string> Owned => Parents.SelectMany(p => p.Copies.Select(c => c.Target)).Concat(Total is null ? [] : [Total.Target]);
    }

    /// <summary> The helper above the routes. </summary>
    public static string Helper(Names n)
    {
        var o = new StringBuilder();
        o.Append("def fill_snapshot(session: Session, row, is_new: bool) -> None:\n");
        o.Append("    \"\"\"What the row copies of its parents is filled when it is created (an update keeps the stored copies: the routes skip them); the total is worked out on every save.\"\"\"\n");
        int p = 0;
        if (n.Parents.Count > 0)
            o.Append("    if is_new:\n");
        foreach (var parent in n.Parents)
        {
            string indent = "        ";
            if (parent.LinkIsNullable)
            {
                o.Append($"{indent}if row.{parent.Link} is not None:\n");
                indent += "    ";
            }
            o.Append($"{indent}parent{p} = session.scalars(select({parent.Class}).where({parent.Class}.{parent.Key} == row.{parent.Link})).first()\n");
            o.Append($"{indent}if parent{p} is None:\n");
            o.Append($"{indent}    raise ApiError(400, \"{RowShapeWords.SnapshotParentTitle}\", problem_type=\"{RowShapeWords.SnapshotParentType}\", detail=\"{RowShapeWords.SnapshotParentMissing(parent.Table)}\")\n");
            foreach (var c in parent.Copies)
                o.Append($"{indent}row.{c.Target} = parent{p}.{c.Source}\n");
            p++;
        }
        if (n.Total is { } total)
            o.Append($"    row.{total.Target} = row.{total.FactorA} * row.{total.FactorB}\n");
        return o.ToString();
    }
}
