namespace CodeGenNew.Core;

/// <summary> The statement behind the grid of a junction table's links page: every existing link as the anchor's key and text and the target's key and text, in the order of the two texts. The columns are
/// named <c>AnchorId</c>, <c>Anchor</c>, <c>TargetId</c> and <c>Target</c> so a typed raw query can read them. Used by the ASP.NET API when it runs routines instead of LINQ, and by the Rust API. </summary>
public sealed class JunctionAllSql
{
    public const string AnchorIdAlias = "AnchorId";
    public const string AnchorAlias = "Anchor";
    public const string TargetIdAlias = "TargetId";
    public const string TargetAlias = "Target";

    private readonly TableModel _model;
    private readonly DialectInfo _dialect;

    public JunctionAllSql(TableModel model)
    {
        _model = model;
        _dialect = DialectInfo.For(model.Dialect);
    }

    private string TextType => _model.Dialect switch
    {
        SqlDialect.SqlServer => "NVARCHAR(450)",
        SqlDialect.MySql => "CHAR",
        _ => "TEXT"
    };

    /// <summary> The text of a row of one side: its display columns cast to text and joined by a space, or its key when the table names none. </summary>
    private string Text(string alias, ForeignKeyModel side)
    {
        var columns = side.ReferencedDisplayDbColumns.Count > 0 ? side.ReferencedDisplayDbColumns : side.ReferencedDbColumns.Take(1).ToList();
        var parts = columns.Select(c => $"CAST({alias}.{_dialect.Quote(c)} AS {TextType})").ToList();
        return parts.Count == 1 ? parts[0] : "CONCAT(" + string.Join(", ' ', ", parts) + ")";
    }

    public const string OrderBy = " ORDER BY 2, 4";

    /// <summary> The statement as plain SQL text (not yet escaped for a string literal). </summary>
    public string Sql => Base + OrderBy;

    /// <summary> The statement without its ORDER BY, so a caller can add a WHERE on the side tables (<c>a</c> is the anchor's, <c>t</c> the target's) first. </summary>
    public string Base
    {
        get
        {
            var junction = _model.Junction!;
            var anchor = junction.AnchorForeignKey;
            var target = junction.TargetForeignKey;
            string links = _dialect.QuoteTable(_model.SchemaName, _model.DbTableName);
            string anchors = _dialect.QuoteTable(anchor.ReferencedSchema, anchor.ReferencedDbTable);
            string targets = _dialect.QuoteTable(target.ReferencedSchema, target.ReferencedDbTable);
            string anchorColumn = _dialect.Quote(junction.AnchorColumn.DbName);
            string targetColumn = _dialect.Quote(junction.TargetColumn.DbName);
            return $"SELECT j.{anchorColumn} AS {_dialect.Quote(AnchorIdAlias)}, {Text("a", anchor)} AS {_dialect.Quote(AnchorAlias)}, "
                + $"j.{targetColumn} AS {_dialect.Quote(TargetIdAlias)}, {Text("t", target)} AS {_dialect.Quote(TargetAlias)} "
                + $"FROM {links} j JOIN {anchors} a ON a.{_dialect.Quote(anchor.ReferencedDbColumns[0])} = j.{anchorColumn} "
                + $"JOIN {targets} t ON t.{_dialect.Quote(target.ReferencedDbColumns[0])} = j.{targetColumn}";
        }
    }
}
