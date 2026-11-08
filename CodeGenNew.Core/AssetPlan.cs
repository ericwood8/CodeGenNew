namespace CodeGenNew.Core;

/// <summary> The words of an asset table (a table that points at a stored file): the problem types and sentences of the upload and download routes, the same wherever they are written. </summary>
public static class AssetWords
{
    public const string NoFileType = ProblemTypes.Prefix + "asset-no-file";
    public const string NoFile = "Send the file as a form field named file.";

    public const string TooLargeType = ProblemTypes.Prefix + "asset-too-large";
    public const string TypeNotAllowedType = ProblemTypes.Prefix + "asset-type-not-allowed";
    public const string FileMissingType = ProblemTypes.Prefix + "asset-file-missing";
    public const string FileMissing = "The row exists but its file is not in the store.";

    /// <summary> What a row made without an upload holds in its storage key: no file is behind it, and the key cannot name another row's file. </summary>
    public const string UnattachedPrefix = "unattached-";

    /// <summary> The room a storage key needs: the longest key is <c>unattached-</c> or 32 hex digits plus a dot and up to 8 letters, and a guid without hyphens is 32. </summary>
    public const int MinKeyLength = 44;

    public const string DefaultFolder = "App_Data/assets";
    public const int DefaultMaxBytes = 10485760;
    public const string OctetStream = "application/octet-stream";

    public static string TooLarge(int maxBytes) => $"The file is larger than {maxBytes} bytes.";

    public static string TypeNotAllowed(string[] allowed) => "Only these file types are accepted: " + string.Join(", ", allowed) + ".";
}

/// <summary> A table the project lists in <c>AssetTables</c>: the row describes a stored file (its key in the store, the name it had, its type and size) and never holds the bytes. <c>Url</c> (optional) holds the address
/// of the download route. </summary>
public sealed record AssetShape(TableModel Table, ColumnModel Key, ColumnModel StorageKey, ColumnModel FileName, ColumnModel MimeType, ColumnModel Size, ColumnModel? Url);

/// <summary> <c>AssetTables=Asset</c> or <c>Asset:StorageKey:FileName:MimeType:Size:Url</c> (the table, then the columns of the storage key, file name, type, size in bytes and the optional download address; each defaults to the column
/// of that name): the row points at a file in a store and never holds the bytes. <c>POST upload</c> takes a multipart form with a field named <c>file</c>, checks its size (<c>AssetMaxBytes</c>) and type (<c>AssetAllowedTypes</c>),
/// writes it to the store under a key the server makes, and answers the new row. <c>GET {id}/download</c> streams the file. A delete removes the file and then the row. A save through the ordinary create and update routes cannot name a
/// storage key: a row made that way holds an unattached key, and an update keeps the stored key, type and size. The store is <c>IAssetStore</c>: a local folder (<c>AssetStoreFolder</c>) unless the project registers its own. </summary>
public static class AssetPlan
{
    public static IReadOnlyList<(string Table, string? Key, string? Name, string? Mime, string? Size, string? Url)> Entries(ProjectSettings project) =>
        SettingParts.Items(project.AssetTables).Select(p => (p[0], SettingParts.At(p, 1), SettingParts.At(p, 2), SettingParts.At(p, 3), SettingParts.At(p, 4), SettingParts.At(p, 5))).ToList();

    public static bool IsListed(string table, ProjectSettings project) => Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    public static AssetShape? For(TableModel table, ProjectSettings project) => IsListed(table.TableName, project) && Resolve(table, project, out _) is { } asset ? asset : null;

    /// <summary> Why a listed table cannot be an asset table, or null when it can (or is not listed). </summary>
    public static string? Problem(TableModel table, ProjectSettings project)
    {
        if (!IsListed(table.TableName, project))
            return null;
        Resolve(table, project, out string? problem);
        return problem;
    }

    /// <summary> The file types the upload accepts, lower case (<c>image/png</c> or <c>image/*</c>); empty for any type. </summary>
    public static string[] AllowedTypes(ProjectSettings project) => project.AssetAllowedTypes.Select(t => t.ToLowerInvariant()).ToArray();

    /// <summary> Whether a type (<c>image/png</c>) is accepted by the list of allowed types. </summary>
    public static bool Accepts(string[] allowed, string mime) =>
        allowed.Length == 0 || allowed.Any(a => a.EndsWith("/*", StringComparison.Ordinal) ? mime.StartsWith(a[..^1], StringComparison.OrdinalIgnoreCase) : a.Equals(mime, StringComparison.OrdinalIgnoreCase));

    private static AssetShape? Resolve(TableModel table, ProjectSettings project, out string? problem)
    {
        problem = null;
        var entry = Entries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        string prefix = $"AssetTables: {table.TableName}";
        if (RowShapeWords.NeverChanged(table, project) is { } never)
        {
            problem = $"{prefix} {never}, so it has no files to store.";
            return null;
        }
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key])
        {
            problem = $"{prefix} needs a single whole-number key.";
            return null;
        }
        if (project.SoftDeleteTables.Any(t => t.Split(':')[0].Trim().EqualsIgnoreCase(table.TableName)))
        {
            problem = $"{prefix} is also in SoftDeleteTables: a delete removes the file, and a soft delete keeps the row.";
            return null;
        }
        if (OwnedTable.IsScoped(table, project))
        {
            problem = $"{prefix} is limited to an owner (OwnedTables): the download route does not check the owner yet.";
            return null;
        }
        if (TenantPlan.OwnedBy(table, project) is not null)
        {
            problem = $"{prefix} belongs to a tenant: the upload route does not write the tenant column yet. List it in NoTenantTables.";
            return null;
        }
        var storage = Named(table, entry.Key, "StorageKey", "FileKey", "Key");
        var name = Named(table, entry.Name, "FileName", "Name", "OriginalName");
        var mime = Named(table, entry.Mime, "MimeType", "ContentType", "FileType");
        var size = Named(table, entry.Size, "SizeBytes", "FileSize", "Size", "Length");
        if (storage is not { IsStringColumn: true, IsNullable: false } || storage.IsPrimaryKey)
        {
            problem = $"{prefix} needs a text column for the storage key that cannot be NULL ({Syntax(table)}).";
            return null;
        }
        if (storage.CharacterLength is > 0 and < AssetWords.MinKeyLength)
        {
            problem = $"{prefix}: {storage.Name} holds {storage.CharacterLength} characters; a storage key needs room for {AssetWords.MinKeyLength}.";
            return null;
        }
        if (name is not { IsStringColumn: true, IsNullable: false } || name.IsPrimaryKey)
        {
            problem = $"{prefix} needs a text column for the file name that cannot be NULL ({Syntax(table)}).";
            return null;
        }
        if (mime is not { IsStringColumn: true } || mime.IsPrimaryKey)
        {
            problem = $"{prefix} needs a text column for the file type ({Syntax(table)}).";
            return null;
        }
        if (size is not { IsIntegerColumn: true } || size.IsPrimaryKey)
        {
            problem = $"{prefix} needs a whole-number column for the size in bytes ({Syntax(table)}).";
            return null;
        }
        var url = entry.Url is null ? null : Named(table, entry.Url);
        if (entry.Url is not null && url is not { IsStringColumn: true })
        {
            problem = $"{prefix}: {entry.Url} is not a text column ({Syntax(table)}).";
            return null;
        }
        var fixedColumns = new[] { key, storage, name, mime, size }.Concat(url is null ? [] : [url]).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var column in table.Columns.Where(c => !fixedColumns.Contains(c.Name)))
            if (!column.IsNullable && !column.IsIdentity && !column.IsComputed && !column.IsRowVersionColumn && column.DatabaseDefaultSql is null)
            {
                problem = $"{prefix}: the upload route cannot fill {column.Name}. Give it a default or let it allow NULL.";
                return null;
            }
        return new AssetShape(table, key, storage, name, mime, size, url);
    }

    private static string Syntax(TableModel table) => $"AssetTables={table.TableName}:StorageKeyColumn:FileNameColumn:MimeTypeColumn:SizeColumn:UrlColumn";

    private static ColumnModel? Named(TableModel table, string? given, params string[] defaults)
    {
        if (given is not null)
            return table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(given));
        foreach (string name in defaults)
            if (table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name)) is { } column)
                return column;
        return null;
    }
}
