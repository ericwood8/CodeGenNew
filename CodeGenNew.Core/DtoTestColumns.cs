namespace CodeGenNew.Core;

/// <summary> The columns the database-free tests of the transfer class (<c>CS_RoundTripTest</c>, <c>CS_MapperlyTest</c>) fill and compare: every column in table order, the ones a comparison covers (the user table's password hash is never written
/// to an answer, so it is not compared) and the ones worth changing to see a difference noticed (not a key, an identity, a computed, audit or stamp column, or the hash). </summary>
public sealed class DtoTestColumns
{
    private DtoTestColumns(TableModel model, ProjectSettings project)
    {
        All = model.Columns.OrderBy(c => c.OrdinalPosition).ToList();
        HashColumn = UserTable.PlanOf(model, project)?.PasswordHash.Name;
        Checked = All.Where(c => !IsHash(c)).ToList();
        Meaningful = All.Where(c => !c.IsPrimaryKey && !c.IsIdentity && !c.IsComputed && !c.IsAuditColumn && !c.IsStampColumn && !IsHash(c)).ToList();
        Changed = Meaningful.FirstOrDefault();
    }

    public static DtoTestColumns Of(TableModel model, ProjectSettings project) => new(model, project);

    public IReadOnlyList<ColumnModel> All { get; }

    /// <summary> The name of the user table's password hash column, or null. </summary>
    public string? HashColumn { get; }

    public IReadOnlyList<ColumnModel> Checked { get; }
    public IReadOnlyList<ColumnModel> Meaningful { get; }

    /// <summary> The column the change step edits; null when none is worth it. </summary>
    public ColumnModel? Changed { get; }

    private bool IsHash(ColumnModel c) => HashColumn is not null && c.Name.EqualsIgnoreCase(HashColumn);
}
