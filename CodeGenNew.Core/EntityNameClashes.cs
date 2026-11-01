namespace CodeGenNew.Core;

/// <summary> Tables whose entity class has the name of a type that the generated projects import by default (a table named Attribute against System.Attribute): the bare name is then ambiguous in every generated file
/// (CS0104). A global using alias to the entity settles it everywhere, since an alias wins over a type a namespace import brings in. </summary>
public static class EntityNameClashes
{
    private static readonly string[] Namespaces =
        ["System", "System.IO", "System.Linq", "System.Threading", "System.Threading.Tasks", "System.Collections.Generic", "System.Net.Http", "System.ComponentModel.DataAnnotations",
         "System.ComponentModel.DataAnnotations.Schema"];

    private static readonly Lazy<HashSet<string>> Known = new(() =>
    {
        var assemblies = new[] { typeof(object), typeof(Enumerable), typeof(Task), typeof(List<>), typeof(System.Net.Http.HttpClient), typeof(System.ComponentModel.DataAnnotations.KeyAttribute) }.Select(t => t.Assembly).Distinct();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assembly in assemblies)
            foreach (var type in assembly.GetExportedTypes())
                if (type.Namespace is { } ns && Namespaces.Contains(ns) && !type.IsNested)
                    names.Add(type.Name.Contains('`') ? type.Name[..type.Name.IndexOf('`')] : type.Name);
        return names;
    });

    /// <summary> The entity names of the tables that clash, in name order. </summary>
    public static IReadOnlyList<string> Of(DatabaseModel database) =>
        [.. database.Tables.Select(t => t.TableName).Where(Known.Value.Contains).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary> The text of a file of global using aliases, or empty when no table clashes. </summary>
    public static string File(DatabaseModel database, string entityNamespace)
    {
        var names = Of(database);
        if (names.Count == 0)
            return "";
        var o = new System.Text.StringBuilder("// A table named like a type the project imports (System.Attribute) would make the bare name ambiguous in every generated file; the alias names the entity.\n");
        foreach (string name in names)
            o.Append($"global using {name} = {entityNamespace}.{name};\n");
        return o.ToString();
    }
}
