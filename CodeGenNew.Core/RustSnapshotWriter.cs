using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Rust (axum, sqlx) counterpart of the snapshot part of <see cref="RowShapeWriter"/>: a table listed in <c>SnapshotColumns</c> copies columns of its parents when a row is created, keeps them when it is
/// changed and works its line total out on every save. <c>fill_snapshot</c> is written into the table's own router and called before the row is written. </summary>
public static class RustSnapshotWriter
{
    public sealed record Copy(string Target, string SourceColumn);

    /// <summary> The copies that come from one parent: the link field, whether it may be empty, the parent's quoted table and key column, and the table's name for the sentence. </summary>
    public sealed record Parent(string LinkField, bool LinkIsOption, string Table, string KeyColumn, string Name, IReadOnlyList<Copy> Copies);

    /// <summary> The total: the target field and the two factors as Rust expressions of the row, already converted to the target's type. </summary>
    public sealed record Total(string Target, string FactorA, string FactorB);

    public sealed record Names(string Struct, IReadOnlyList<Parent> Parents, Total? Total)
    {
        public IEnumerable<string> Owned => Parents.SelectMany(p => p.Copies.Select(c => c.Target)).Concat(Total is null ? [] : [Total.Target]);
    }

    /// <summary> The factor as an expression of <paramref name="row"/> in the type of <paramref name="target"/>. </summary>
    public static string Factor(RustField field, RustField target, string row)
    {
        string value = field.IsOption ? $"{row}.{field.Name}.unwrap_or_default()" : $"{row}.{field.Name}";
        if (field.BaseType == target.BaseType)
            return value;
        return target.BaseType == "Decimal" ? $"rust_decimal::Decimal::from({value})" : $"({value} as {target.BaseType})";
    }

    public static string Code(Names n)
    {
        var o = new StringBuilder();
        o.Append("/// What the row copies of its parents is filled when it is created (an update puts the stored copies back); the total is worked out on every save.\n");
        o.Append($"async fn fill_snapshot(pool: &Pool, row: &mut {n.Struct}, stored: Option<&{n.Struct}>) -> ApiResult<()> {{\n");
        o.Append("    use sqlx::Row as _;\n");
        o.Append("    if let Some(stored) = stored {\n");
        foreach (string target in n.Parents.SelectMany(p => p.Copies.Select(c => c.Target)))
            o.Append($"        row.{target} = stored.{target}.clone();\n");
        o.Append("    } else {\n");
        foreach (var parent in n.Parents)
        {
            string select = string.Join(", ", parent.Copies.Select(c => c.SourceColumn));
            string indent = "        ";
            string value = $"row.{parent.LinkField}.clone()";
            if (parent.LinkIsOption)
            {
                o.Append($"{indent}if let Some(link) = row.{parent.LinkField}.clone() {{\n");
                indent += "    ";
                value = "link";
            }
            o.Append($"{indent}let mut builder = sqlx::QueryBuilder::<crate::support::Db>::new({RustTable.Raw($"SELECT {select} FROM {parent.Table} WHERE {parent.KeyColumn} = ")});\n");
            o.Append($"{indent}builder.push_bind({value});\n");
            o.Append($"{indent}let Some(parent) = builder.build().fetch_optional(pool).await? else {{\n");
            o.Append($"{indent}    return Err(ApiError::Rejected(400, \"{RowShapeWords.SnapshotParentType}\", \"{RowShapeWords.SnapshotParentTitle}\", \"{RowShapeWords.SnapshotParentMissing(parent.Name)}\".into()));\n");
            o.Append($"{indent}}};\n");
            int i = 0;
            foreach (var c in parent.Copies)
                o.Append($"{indent}row.{c.Target} = parent.try_get({i++})?;\n");
            if (parent.LinkIsOption)
                o.Append("        }\n");
        }
        o.Append("    }\n");
        if (n.Total is { } total)
            o.Append($"    row.{total.Target} = {total.FactorA} * {total.FactorB};\n");
        o.Append("    Ok(())\n}\n");
        return o.ToString();
    }
}
