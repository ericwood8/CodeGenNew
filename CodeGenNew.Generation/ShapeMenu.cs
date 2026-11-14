using CodeGenNew.Core;

namespace CodeGenNew.Generation;

/// <summary> What the right-click menu offers on one table, worked out from the whole database so the desktop app only draws it. </summary>
public static class ShapeMenu
{
    /// <summary> The shapes <paramref name="tableName"/> looks like that <paramref name="project"/> does not name: the table's own and the ones that need the other tables, both from
    /// <see cref="DatabaseShapeDetector"/>. Empty when the database has no such table. </summary>
    public static IReadOnlyList<ShapeSuggestion> Suggestions(DatabaseModel database, string tableName, ProjectSettings project)
    {
        var table = database.Tables.FirstOrDefault(t => t.TableName == tableName || t.DbTableName == tableName);
        if (table is null)
            return [];
        var served = project.ForServer(database.ServerVersion);
        return [.. DatabaseShapeDetector.Of(database, served).Where(d => d.Table == table.TableName).Select(d => d.Suggestion)];
    }

    /// <summary> The text of the project file with the suggestion's item added to its setting; every other line stays as it is. </summary>
    public static string Apply(string projectFileText, ShapeSuggestion suggestion) => ProjectFileEdit.AddListItem(projectFileText, suggestion.Setting, suggestion.Item);
}
