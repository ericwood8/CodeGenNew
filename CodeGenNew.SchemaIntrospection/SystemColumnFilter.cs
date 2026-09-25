using CodeGenNew.Core;

namespace CodeGenNew.SchemaIntrospection;

/// <summary> Excludes SQL Server replication housekeeping columns -- these are added
/// by replication itself, never something a developer inserts/updates, so they're filtered from both the
/// TreeView's column display and the columns BuildTableModelAsync hands to the Insert/Update templates. </summary>
public static class SystemColumnFilter
{
    private static readonly string[] ExactNames = ["msrepl_tran_version"];

    public static bool IsSystemColumn(this string columnName) =>
        ExactNames.Any(n => columnName.EqualsIgnoreCase(n));
}
