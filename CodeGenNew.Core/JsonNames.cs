namespace CodeGenNew.Core;

/// <summary> The property names ASP.NET Core's JSON serializer gives a C# property (camel-casing), so a schema or client written from a column matches what the API really sends. </summary>
public static class JsonNames
{
    /// <summary> <c>IsActive</c> gives <c>isActive</c>, <c>SY_Role</c> gives <c>sY_Role</c>, <c>URL</c> gives <c>url</c>: the leading run of capitals is lowered, up to the last one that starts a word. </summary>
    public static string Camel(string name)
    {
        if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0]))
            return name;

        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (i == 1 && !char.IsUpper(chars[i]))
                break;
            bool hasNext = i + 1 < chars.Length;
            if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
                break;
            chars[i] = char.ToLowerInvariant(chars[i]);
        }
        return new string(chars);
    }
}
