namespace CodeGenNew.Core;

/// <summary> A shape a table looks like but the project does not name. <see cref="Item"/> is what would be added to <see cref="Setting"/> (empty setting: the shape needs none).
/// <see cref="Fits"/> is true when the shape's own planner accepts the table as if it were listed; otherwise <see cref="Note"/> says what is missing. </summary>
public sealed record ShapeSuggestion(string Shape, string Setting, string Item, bool Fits, string Note)
{
    public bool CanBeApplied => Fits && Setting.Length > 0;

    public string Text => Setting.Length > 0 ? $"{Shape} ({Setting}={Item}): {Note}" : $"{Shape}: {Note}";
}

/// <summary> Finds the shapes of a table from its columns and keys, with the usual names, and never changes what is generated: the answer is a suggestion for the menu and the run report.
/// A shape fits when its planner accepts the table listed with the suggested item, so the suggestion and the run cannot disagree. </summary>
public static class TableShapeDetector
{
    private static readonly string[] SoftDeleteFlags = ["IsDeleted", "Deleted", "IsRemoved", "Removed", "IsVoided", "Voided", "IsArchived"];
    private static readonly string[] SoftDeleteStamps = ["DeletedOn", "DeletedAt", "DeletedDate"];
    private static readonly string[] DefaultFlags = ["IsDefault", "Default", "IsPrimary", "Primary", "IsMain", "IsPreferred", "IsCurrent"];
    private static readonly string[] GroupKindSuffixes = ["Type", "Kind"];
    private static readonly (string From, string To)[] PeriodPairs =
        [("ValidFrom", "ValidTo"), ("EffectiveFrom", "EffectiveTo"), ("StartDate", "EndDate"), ("FromDate", "ToDate"), ("StartsOn", "EndsOn"), ("StartOn", "EndOn"), ("StartDateTime", "EndDateTime")];
    private static readonly string[] QueueStatusNames = ["Status", "State"];
    private static readonly string[] QueueRetryNames = ["RetryCount", "Attempts", "Tries", "AttemptCount"];
    private static string[] QueueClaimStems => QueuePlan.ClaimStems;
    private static readonly string[] QueueEndStems = ["Processed", "Completed", "Finished", "Done"];

    public static IReadOnlyList<ShapeSuggestion> Of(TableModel table, ProjectSettings project)
    {
        var found = new List<ShapeSuggestion>();
        if (Extension(table, project) is { } extension)
            found.Add(extension);
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key])
            return found;
        foreach (var suggestion in new[] { Tree(table, project, key), SoftDelete(table, project), DefaultRow(table, project, key), Effective(table, project), Queue(table, project),
            Content(table, project), Asset(table, project), Inbound(table, project), Token(table, project), Usage(table, project) })
            if (suggestion is not null)
                found.Add(suggestion);
        return found;
    }

    private static ShapeSuggestion? Extension(TableModel table, ProjectSettings project) =>
        ExtensionPlan.For(table, project) is { } shape
            ? new ShapeSuggestion("One-to-one extension", "", "", true, $"its key is also its foreign key to {shape.Base.ReferencedTable}, so it adds columns to a row of {shape.Base.ReferencedTable}")
            : null;

    private static ShapeSuggestion? Tree(TableModel table, ProjectSettings project, ColumnModel key)
    {
        if (TreePlan.IsListed(table.TableName, project))
            return null;
        var own = table.ForeignKeys.Where(f => f.ReferencedTable.EqualsIgnoreCase(table.TableName) && f.ReferencingColumns.Count == 1 && f.ReferencedColumns.Count == 1
            && f.ReferencedColumns[0].EqualsIgnoreCase(key.Name) && !f.ReferencingColumns[0].EqualsIgnoreCase(key.Name)).Select(f => f.ReferencingColumns[0])
            .Where(name => table.Columns.Any(c => c.Name.EqualsIgnoreCase(name) && c.IsNullable)).ToList();
        if (own.Count == 0)
            return null;
        if (own.Count > 1)
            return new ShapeSuggestion("Tree", "TreeTables", $"{table.TableName}:{own[0]}", false, $"has {own.Count} foreign keys to itself ({string.Join(", ", own)}); name the parent column");
        return Judge("Tree", "TreeTables", table.TableName, "refers to its own rows through " + own[0], table, project, project.TreeTables, TreePlan.Problem);
    }

    private static ShapeSuggestion? SoftDelete(TableModel table, ProjectSettings project)
    {
        if (SoftDeletePlan.IsListed(table.TableName, project) || RowShapeWords.NeverChanged(table, project) is not null)
            return null;
        var flag = table.Columns.FirstOrDefault(c => c.IsBooleanColumn && SoftDeleteFlags.Any(n => c.Name.EqualsIgnoreCase(n)));
        if (flag is null)
        {
            var stamp = table.Columns.FirstOrDefault(c => c.IsNullable && SoftDeleteStamps.Any(n => c.Name.EqualsIgnoreCase(n)));
            return stamp is null ? null
                : new ShapeSuggestion("Soft delete", "SoftDeleteTables", table.TableName, false, $"{stamp.Name} allows NULL as its mark of a deleted row, but SoftDeleteTables takes a yes/no column (default {SoftDeletePlan.DefaultFlag}); add one");
        }
        string item = flag.Name.EqualsIgnoreCase(SoftDeletePlan.DefaultFlag) ? table.TableName : $"{table.TableName}:{flag.Name}";
        return Judge("Soft delete", "SoftDeleteTables", item, $"{flag.Name} marks a row as deleted", table, project, project.SoftDeleteTables, SoftDeletePlan.Problem);
    }

    private static ShapeSuggestion? DefaultRow(TableModel table, ProjectSettings project, ColumnModel key)
    {
        if (DefaultRowPlan.IsListed(table.TableName, project))
            return null;
        var flag = table.Columns.FirstOrDefault(c => c.IsBooleanColumn && DefaultFlags.Any(n => c.Name.EqualsIgnoreCase(n)));
        if (flag is null)
            return null;
        var parents = table.ForeignKeys.Where(f => f.ReferencingColumns.Count == 1).Select(f => f.ReferencingColumns[0])
            .Where(n => !n.EqualsIgnoreCase(key.Name) && !n.EqualsIgnoreCase(flag.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (parents.Count == 0)
            return null;
        string kind = table.Columns.Where(c => !c.IsPrimaryKey && (c.IsStringColumn || c.IsIntegerColumn) && !parents.Any(p => p.EqualsIgnoreCase(c.Name))
            && GroupKindSuffixes.Any(s => c.Name.EndsWithIgnoreCase(s))).Select(c => c.Name).FirstOrDefault() is { } k ? "+" + k : "";
        string item = $"{table.TableName}:{flag.Name}:{parents[0]}{kind}";
        if (parents.Count > 1)
            return new ShapeSuggestion("Default row", "DefaultRowTables", item, false, $"{flag.Name} marks one row per group, but {string.Join(" and ", parents)} could each be the group; name the group columns");
        bool oneAllowed = table.PartialIndexes.Any(i => i.IsUnique && i.Filter.Contains(flag.DbName, StringComparison.OrdinalIgnoreCase));
        string why = $"{flag.Name} marks one row per {parents[0]}" + (oneAllowed ? ", and a unique filtered index allows only one" : "");
        return Judge("Default row", "DefaultRowTables", item, why, table, project, project.DefaultRowTables, DefaultRowPlan.Problem);
    }

    private static ShapeSuggestion? Effective(TableModel table, ProjectSettings project)
    {
        if (EffectiveDatePlan.IsListed(table.TableName, project) || RowShapeWords.NeverChanged(table, project) is not null)
            return null;
        var parents = table.ForeignKeys.Where(f => f.ReferencingColumns.Count == 1).Select(f => f.ReferencingColumns[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (parents.Count == 0)
            return null;
        var ordered = table.CheckConstraints.Select(k => CheckConstraintParser.DateOrder(k.Definition)).OfType<(string Start, string End)>()
            .Select(o => (First: table.Columns.FirstOrDefault(c => c.IsDateColumn && c.DbName.EqualsIgnoreCase(o.Start)), Last: table.Columns.FirstOrDefault(c => c.IsDateColumn && c.DbName.EqualsIgnoreCase(o.End))))
            .Where(p => p.First is not null && p.Last is not null).Select(p => (First: p.First!, Last: p.Last!)).ToList();
        var pairs = ordered.Concat(PeriodPairs.Select(p => (First: table.Columns.FirstOrDefault(c => c.IsDateColumn && c.Name.EqualsIgnoreCase(p.Item1)),
            Last: table.Columns.FirstOrDefault(c => c.IsDateColumn && c.Name.EqualsIgnoreCase(p.Item2)))).Where(p => p.First is not null && p.Last is not null).Select(p => (First: p.First!, Last: p.Last!)));
        foreach (var (first, last) in pairs)
        {
            bool checkedByDatabase = ordered.Any(o => o.First == first && o.Last == last);
            string item = $"{table.TableName}:{first.Name}:{last.Name}" + (parents.Count == 1 ? ":" + parents[0] : "");
            string why = $"{first.Name} to {last.Name} is a period of {string.Join(" and ", parents)}" + (checkedByDatabase ? $", and a check constraint keeps {first.Name} from being after {last.Name}" : "");
            return Judge("Effective dated rows", "EffectiveDatedTables", item, why, table, project, project.EffectiveDatedTables, EffectiveDatePlan.Problem);
        }
        return null;
    }

    private static ShapeSuggestion? Queue(TableModel table, ProjectSettings project)
    {
        if (QueuePlan.IsListed(table.TableName, project))
            return null;
        var status = table.Columns.FirstOrDefault(c => c.IsStringColumn && QueueStatusNames.Any(n => c.Name.EqualsIgnoreCase(n)));
        var retry = table.Columns.FirstOrDefault(c => c.IsIntegerColumn && !c.IsPrimaryKey && QueueRetryNames.Any(n => c.Name.EqualsIgnoreCase(n)));
        bool claim = table.Columns.Any(c => LogTablePlan.HasTimeOfDay(c) && QueueClaimStems.Any(s => c.Name.StartsWithIgnoreCase(s)) && !c.Name.EndsWithIgnoreCase("By"));
        bool end = table.Columns.Any(c => LogTablePlan.HasTimeOfDay(c) && QueueEndStems.Any(s => c.Name.StartsWithIgnoreCase(s)));
        if (status is null || retry is null || !claim || !end)
            return null;
        string item = status.Name.EqualsIgnoreCase(QueuePlan.DefaultStatus) && retry.Name.EqualsIgnoreCase(QueuePlan.DefaultRetry) ? table.TableName : $"{table.TableName}:{status.Name}:{retry.Name}";
        return Judge("Work queue", "QueueTables", item, $"{status.Name}, {retry.Name}, a claim stamp and an end stamp", table, project, project.QueueTables, QueuePlan.Problem);
    }

    private static readonly string[] SlugNames = ["Slug", "UrlName", "Permalink", "Handle"];
    private static readonly string[] TitleNames = ["Title", "Name", "Headline"];
    private static readonly string[] AssetKeyNames = ["StorageKey", "StoragePath", "BlobKey", "FilePath", "Path", "Key"];
    private static readonly string[] AssetNameNames = ["FileName", "OriginalName", "Filename"];
    private static readonly string[] AssetTypeNames = ["MimeType", "ContentType", "MediaType"];
    private static readonly string[] AssetSizeNames = ["Size", "SizeBytes", "ByteSize", "Length", "FileSize"];
    private static readonly string[] InboundBodyNames = ["Body", "Payload", "RawBody", "Json", "Message"];
    private static readonly string[] InboundEventNames = ["EventId", "ExternalId"];
    private static readonly string[] InboundReceivedNames = ["ReceivedOn", "ReceivedAt"];
    private static readonly string[] InboundDoneNames = ["Processed", "IsProcessed", "ProcessedOn", "ProcessedAt"];
    private static readonly string[] TokenExpiryStems = ["Expires", "Expiry"];
    private static readonly string[] UsageKindNames = ["Kind", "EventType", "Metric", "Meter", "Feature"];
    private static readonly string[] UsageQuantityNames = ["Quantity", "Units", "Amount", "Count"];
    private static readonly string[] UsageOccurredNames = ["OccurredOn", "RecordedAt", "UsedOn"];

    private static ColumnModel? Named(TableModel table, string[] names, Func<ColumnModel, bool> kind) =>
        table.Columns.FirstOrDefault(c => !c.IsPrimaryKey && kind(c) && names.Any(n => c.Name.EqualsIgnoreCase(n)));

    private static ShapeSuggestion? Content(TableModel table, ProjectSettings project)
    {
        if (ContentPlan.IsListed(table.TableName, project))
            return null;
        var slug = Named(table, SlugNames, c => c.IsStringColumn);
        var title = Named(table, TitleNames, c => c.IsStringColumn);
        var status = Named(table, ["Status", "State"], c => c.IsStringColumn);
        if (slug is null || title is null)
            return null;
        bool unique = table.Indexes.Any(i => i.IsUnique && i.Columns is [var only] && only.EqualsIgnoreCase(slug.Name));
        string item = slug.Name.EqualsIgnoreCase("Slug") && title.Name.EqualsIgnoreCase("Title") && status?.Name.EqualsIgnoreCase("Status") != false ? table.TableName
            : $"{table.TableName}:{title.Name}:{slug.Name}:{status?.Name ?? "Status"}";
        var shape = Judge("Content with a slug", "ContentTables", item, $"{slug.Name} beside {title.Name}", table, project, project.ContentTables, ContentPlan.Problem);
        return unique ? shape : shape with { Fits = false, Note = shape.Note + (shape.Fits ? ", but" : "; also") + $" {slug.Name} has no unique index, so two rows could share an address" };
    }

    private static ShapeSuggestion? Asset(TableModel table, ProjectSettings project)
    {
        if (AssetPlan.IsListed(table.TableName, project))
            return null;
        var key = Named(table, AssetKeyNames, c => c.IsStringColumn);
        var name = Named(table, AssetNameNames, c => c.IsStringColumn);
        var type = Named(table, AssetTypeNames, c => c.IsStringColumn);
        var size = Named(table, AssetSizeNames, c => c.IsIntegerColumn || c.IsNumericColumn);
        if (new object?[] { key, name, type, size }.Count(c => c is not null) < 3 || type is null)
            return null;
        if (key is null || name is null || size is null)
        {
            var binary = table.Columns.FirstOrDefault(c => c.IsBinaryColumn);
            string held = binary is null ? "" : $"; the file itself is in {binary.Name}, and stored file metadata is for files kept outside the database";
            return new ShapeSuggestion("Stored file metadata", "AssetTables", table.TableName, false,
                $"has {string.Join(", ", new[] { key, name, type, size }.OfType<ColumnModel>().Select(c => c.Name))} but no " + string.Join(" and no ", new[] { key is null ? "storage key" : null, name is null ? "file name" : null, size is null ? "size" : null }.OfType<string>()) + held);
        }
        return Judge("Stored file metadata", "AssetTables", $"{table.TableName}:{key.Name}:{name.Name}:{type.Name}:{size.Name}", "holds a storage key, a file name, a type and a size", table, project, project.AssetTables, AssetPlan.Problem);
    }

    private static ShapeSuggestion? Inbound(TableModel table, ProjectSettings project)
    {
        if (InboundPlan.IsListed(table.TableName, project))
            return null;
        var body = Named(table, InboundBodyNames, c => c.IsStringColumn);
        var received = Named(table, InboundReceivedNames, LogTablePlan.HasTimeOfDay);
        var done = table.Columns.FirstOrDefault(c => InboundDoneNames.Any(n => c.Name.EqualsIgnoreCase(n)) && (c.IsBooleanColumn || LogTablePlan.HasTimeOfDay(c)));
        bool claim = table.Columns.Any(c => LogTablePlan.HasTimeOfDay(c) && QueueClaimStems.Any(s => c.Name.StartsWithIgnoreCase(s)) && !c.Name.EndsWithIgnoreCase("By"));
        if (body is null || received is null || done is null || claim)
            return null;
        var eventId = Named(table, InboundEventNames, c => c.IsStringColumn);
        string item = $"{table.TableName}:{body.Name}" + (eventId is null ? "" : ":" + eventId.Name);
        return Judge("Inbound event table", "InboundTables", item, $"{body.Name}, {received.Name} and {done.Name}", table, project, project.InboundTables, InboundPlan.Problem);
    }

    private static ShapeSuggestion? Token(TableModel table, ProjectSettings project)
    {
        if (TokenPlan.IsListed(table.TableName, project))
            return null;
        var hash = table.Columns.FirstOrDefault(c => c.IsStringColumn && c.Name.ContainsIgnoreCase("Hash") && !c.Name.ContainsIgnoreCase("Password"));
        var expiry = table.Columns.FirstOrDefault(c => LogTablePlan.HasTimeOfDay(c) && TokenExpiryStems.Any(s => c.Name.ContainsIgnoreCase(s)));
        if (hash is null || expiry is null)
            return null;
        return Judge("Session and token table", "TokenTables", $"{table.TableName}:{hash.Name}:{expiry.Name}", $"{hash.Name} and {expiry.Name}", table, project, project.TokenTables, TokenPlan.Problem);
    }

    private static ShapeSuggestion? Usage(TableModel table, ProjectSettings project)
    {
        if (UsagePlan.IsListed(table.TableName, project))
            return null;
        var kind = Named(table, UsageKindNames, c => c.IsStringColumn && !c.IsNullable);
        var quantity = Named(table, UsageQuantityNames, c => (c.IsIntegerColumn || c.IsNumericColumn) && !c.IsNullable);
        var occurred = Named(table, UsageOccurredNames, c => LogTablePlan.HasTimeOfDay(c) && !c.IsNullable);
        if (kind is null || quantity is null || occurred is null)
            return null;
        return Judge("Usage ledger", "UsageTables", $"{table.TableName}:{kind.Name}:{quantity.Name}:{occurred.Name}", $"{kind.Name}, {quantity.Name} and {occurred.Name}", table, project, project.UsageTables, UsagePlan.Problem);
    }

    /// <summary> Runs the shape's own check on the table listed with <paramref name="item"/>; no problem means it fits. </summary>
    private static ShapeSuggestion Judge(string shape, string setting, string item, string why, TableModel table, ProjectSettings project, string[] current, Func<TableModel, ProjectSettings, string?> problem)
    {
        var listed = project.WithOverrides([new(setting, string.Join(",", current.Append(item)))]);
        string? missing = problem(table, listed);
        return missing is null
            ? new ShapeSuggestion(shape, setting, item, true, why)
            : new ShapeSuggestion(shape, setting, item, false, $"{why}, but {missing}");
    }
}
