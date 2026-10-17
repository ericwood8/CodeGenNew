using System.Data;

namespace CodeGenNew.Core;

/// <summary>
/// The separate input and output types of a table's CRUD API (<c>Dtos=true</c>): the output is the table's transfer class (CS_Dto, every column, no navigation) and the input
/// (<c>CS_DtoInput</c>) holds only the columns a caller may set. Every template that names either type asks this one rule.
/// </summary>
public static class DtoShape
{
    /// <summary> True when the table's API takes an input type and answers an output type: <c>Dtos=true</c> and a plain CRUD API of any key (a name/active table keeps its entity). </summary>
    public static bool Uses(TableModel table, ProjectSettings project) => project.Dtos && table.HasCrudApiOfAnyKey(project);

    /// <summary> The columns a caller may set: not computed, not a version stamp, not audit / create / modify, and not an identity key. A key the person chooses (text, Guid) stays, and so does the owner column
    /// of an owned table: the API checks it against the owners the caller may act for (403 for anyone else's), which is what lets the administrator create a row for a person. </summary>
    public static List<ColumnModel> InputColumns(TableModel table, ProjectSettings? project = null)
    {
        // the user table's password hash is set by the set-password command only
        string? hash = project is null ? null : UserTable.PlanOf(table, project)?.PasswordHash.Name;
        return table.Columns.OrderBy(c => c.OrdinalPosition)
            .Where(c => hash is null || !c.Name.EqualsIgnoreCase(hash))
            .Where(c => !c.IsComputed && !c.IsRowVersionColumn
                        && !c.IsAuditColumn && !c.IsStampColumn
                        && !(c.IsPrimaryKey && c.IsIdentity))
            .ToList();
    }
}
