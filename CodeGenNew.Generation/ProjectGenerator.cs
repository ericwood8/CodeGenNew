using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Generation;

public sealed class GenerateOptions
{
    public required ProjectSettings Project { get; init; }
    /// <summary> The stacks to generate (Api, WinUI3, React, Angular, Blazor, Rust). </summary>
    public required IReadOnlyList<string> Stacks { get; init; }
    /// <summary> The folder every stack folder sits under (the stack folders come from the project's OutputApi, OutputWinUI3 ... settings). </summary>
    public required string OutputDirectory { get; init; }
    public required string DatabaseName { get; init; }
    public required string Schema { get; init; }
    public bool DryRun { get; init; }
    /// <summary> Also write the essentials groups of the stacks (the files no table drives): only the missing files, unless <see cref="ReplaceEssentials"/>. </summary>
    public bool Essentials { get; init; }
    /// <summary> The essentials groups to write; null = every group that is ticked by default. </summary>
    public IReadOnlyList<string>? EssentialsGroups { get; init; }
    public bool ReplaceEssentials { get; init; }
    /// <summary> Carry the line diff of every file that exists with other content (a dry run then shows what regenerating would change). </summary>
    public bool WithDiff { get; init; }
    /// <summary> Run only these templates (by name, <c>SP_Search</c> or <c>CS_Entity_v1.tt</c>); null = the whole plan. </summary>
    public IReadOnlyList<string>? OnlyTemplates { get; init; }
    /// <summary> Run only for these tables (by generated or database name): a quick regenerate after one table changed. The templates that cover the whole database (the context, the
    /// registration, the screens list) and the essentials are left out, and no file is reported stale. </summary>
    public IReadOnlyList<string>? Tables { get; init; }
    /// <summary> Delete the stale files (files an earlier run wrote that the plan no longer produces) that nobody has edited since. Edited ones are only reported. </summary>
    public bool DeleteStale { get; init; }
}

/// <summary> One run of one template over one table (or the whole database): its files and how each went. </summary>
public sealed record StepOutcome(string Template, string? Table, IReadOnlyList<FileOutcome> Files);

public sealed class GenerateReport
{
    public List<StepOutcome> Steps { get; } = [];
    /// <summary> A template that declined a table with its reason (a name/active table has no CRUD API ...): reported, not a failure. </summary>
    public List<string> Refusals { get; } = [];
    /// <summary> A template that failed to compile or run. </summary>
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    public EssentialsRun? Essentials { get; set; }
    /// <summary> Files an earlier whole-project run wrote that this run does not produce (a table or template that is gone). Empty after a partial run (--only, --table). </summary>
    public List<StaleFile> Stale { get; } = [];
    /// <summary> True when the run wrote <c>.codegen-manifest.json</c> (not on a dry run); false means the next run has no list to compare with. </summary>
    public bool ManifestWritten { get; set; }

    public IEnumerable<FileOutcome> AllFiles => Steps.SelectMany(s => s.Files).Concat(Essentials?.Outcomes ?? []);
    public int Count(FileOutcomeKind kind) => AllFiles.Count(f => f.Kind == kind);
    public bool Success => Errors.Count == 0 && (Essentials?.Success ?? true);
}

/// <summary> Generates every file of a project in one process: the schema is read once, the templates and tables come from <see cref="ProjectPlan"/>, and files whose text did not change are
/// left alone. This is what each sample's Regenerate.sh did with one command-line run per file. </summary>
public static class ProjectGenerator
{
    public static async Task<GenerateReport> RunAsync(ISchemaProvider provider, string templatesDirectory, GenerateOptions options, Action<string>? progress = null,
        CancellationToken cancellationToken = default, TemplateCache? templateCache = null)
    {
        var report = new GenerateReport();
        var project = options.Project;
        using var ownCache = templateCache is null ? new TemplateCache() : null;
        var cache = templateCache ?? ownCache!;

        progress?.Invoke("Reading the schema...");
        var database = await provider.BuildAsync(options.DatabaseName, options.Schema, cancellationToken);
        progress?.Invoke($"Read {database.Tables.Count} tables of [{options.Schema}].");

        if (options.Stacks.Contains("Rust", StringComparer.OrdinalIgnoreCase) && database.Dialect == SqlDialect.SqlServer)
        {
            report.Errors.Add("The Rust stack is not written for SQL Server: sqlx, the database layer it uses, supports PostgreSQL, MySQL and SQLite. Leave Rust out of the project's Stacks or read one of those databases.");
            return report;
        }

        foreach (var table in database.Tables)
            foreach (var column in table.UnsupportedColumns)
                report.Warnings.Add($"column {table.DbTableName}.{column.DbName} has the type '{column.SqlTypeDeclaration}', which CodeGenNew does not map; list it in IgnoredColumns to leave it out.");

        var templates = TemplateCatalog.Discover(templatesDirectory);
        var steps = ProjectPlan.Build(templates, database, project, options.Stacks);
        bool partial = options.OnlyTemplates is { Count: > 0 } || options.Tables is { Count: > 0 };
        if (options.Tables is { Count: > 0 } wantedTables)
        {
            foreach (string name in wantedTables.Where(n => !database.Tables.Any(t => t.TableName.Equals(n, StringComparison.OrdinalIgnoreCase) || t.DbTableName.Equals(n, StringComparison.OrdinalIgnoreCase))))
                report.Warnings.Add($"--table {name}: no such table in [{options.Schema}].");
            bool Wanted(string table) => database.Tables.Any(t => t.TableName == table && wantedTables.Any(n => n.Equals(t.TableName, StringComparison.OrdinalIgnoreCase) || n.Equals(t.DbTableName, StringComparison.OrdinalIgnoreCase)));
            steps = steps.Where(s => !s.IsDatabaseLevel).Select(s => s with { TableNames = s.TableNames.Where(Wanted).ToList() }).Where(s => s.TableNames.Count > 0).ToList();
        }
        if (options.OnlyTemplates is { Count: > 0 } only)
        {
            var wanted = only.Select(n => TemplateCatalog.ParseName(n.EndsWith(".tt", StringComparison.OrdinalIgnoreCase) ? n[..^3] : n).BaseName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            steps = steps.Where(s => wanted.Contains(s.Template.Name)).ToList();
        }

        // a table's model is built per depth a template asks for (its rows, its parents' display columns); most templates share the one in the database model
        var models = new Dictionary<(string Table, bool Rows, bool Display), TableModel>();
        async Task<TableModel> ModelOf(string tableName, bool rows, bool display)
        {
            var key = (tableName, rows, display);
            if (models.TryGetValue(key, out var cached))
                return cached;
            var model = !rows && !display
                ? database.Tables.First(t => t.TableName == tableName)
                : await provider.BuildTableModelAsync(options.Schema, database.Tables.First(t => t.TableName == tableName).DbTableName, rows, display, cancellationToken);
            return models[key] = model;
        }

        foreach (var step in steps)
        {
            var template = step.Template;
            var config = template.Config;
            if (config.DatabaseOnly)
            {
                progress?.Invoke(template.Name);
                var result = await cache.RunAsync(template.FilePath, database, project, cancellationToken);
                if (!result.Success) { report.Errors.Add($"{template.Name}: {string.Join(" | ", result.Errors)}"); continue; }
                var files = OutputWriter.FilesOf(template, project.ContextName ?? database.DatabaseName + "Context", result.GeneratedText!);
                report.Steps.Add(new StepOutcome(template.Name, null, await WriteForStacksAsync(options, step, files, cancellationToken)));
                continue;
            }

            foreach (string tableName in step.TableNames)
            {
                var model = await ModelOf(tableName, config.NeedsRowData, config.NeedsReferencedDisplayColumns);
                if (config.Refuse(model) is { } refusal)
                {
                    report.Refusals.Add($"{template.Name} for {tableName}: {refusal}");
                    continue;
                }
                progress?.Invoke($"{template.Name} {tableName}");
                var result = await cache.RunAsync(template.FilePath, model, project, cancellationToken);
                if (!result.Success) { report.Errors.Add($"{template.Name} for {tableName}: {string.Join(" | ", result.Errors)}"); continue; }
                var files = OutputWriter.FilesOf(template, tableName, result.GeneratedText!);
                report.Steps.Add(new StepOutcome(template.Name, tableName, await WriteForStacksAsync(options, step, files, cancellationToken)));
            }
        }

        await UpdateManifestAsync(report, options, partial);

        if (options.Essentials && options.Tables is not { Count: > 0 })
        {
            var groups = options.Stacks.SelectMany(s => EssentialsCatalog.Groups(templatesDirectory, s)).ToList();
            if (options.EssentialsGroups is { Count: > 0 } named)
                groups = groups.Where(g => named.Contains(g.Name, StringComparer.OrdinalIgnoreCase)).ToList();
            else
                groups = groups.Where(g => g.DefaultOn).ToList();
            progress?.Invoke("Essentials...");
            report.Essentials = await EssentialsCatalog.GenerateAsync(groups, project, options.OutputDirectory, options.ReplaceEssentials, options.DryRun, options.WithDiff, cancellationToken);
        }
        return report;
    }

    /// <summary> Compares what this run produced with the manifest of the last one: files that are no longer produced are stale; then saves the new manifest (not on a dry run). </summary>
    private static Task UpdateManifestAsync(GenerateReport report, GenerateOptions options, bool partial)
    {
        string output = options.OutputDirectory;
        var produced = new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in report.Steps.SelectMany(s => s.Files).Where(f => f.Kind != FileOutcomeKind.Skipped && f.Content is not null))
        {
            string relative = GenerationManifest.Relative(output, file.FullPath);
            produced[relative] = new ManifestEntry(relative, file.Stack ?? "", GenerationManifest.Hash(file.Content!));
        }

        var manifest = GenerationManifest.Load(output);
        var scope = options.Stacks.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (scope.Contains("Api") || scope.Contains("WinUI3"))
            scope.Add("Sql");
        if (scope.Contains("Api"))
            scope.Add("ApiTests");

        var kept = new List<ManifestEntry>();
        foreach (var old in manifest.Entries)
        {
            if (produced.ContainsKey(old.Path))
                continue;
            if (partial || !scope.Contains(old.Stack))
            {
                kept.Add(old);
                continue;
            }
            string full = Path.GetFullPath(Path.Combine(output, old.Path));
            bool exists = File.Exists(full);
            bool edited = exists && GenerationManifest.Hash(File.ReadAllText(full)) != old.Sha256;
            bool deleted = false;
            if (!exists)
                continue;   // already gone: nothing to report or remember
            if (options.DeleteStale && !options.DryRun && !edited)
            {
                File.Delete(full);
                deleted = true;
            }
            report.Stale.Add(new StaleFile(old.Path, old.Stack, exists, edited, deleted));
            if (!deleted)
                kept.Add(old);   // still on disk: it stays on the list until someone deals with it
        }

        if (!options.DryRun)
        {
            manifest.Entries = [.. kept, .. produced.Values];
            manifest.Save(output);
            report.ManifestWritten = true;
        }
        return Task.CompletedTask;
    }

    /// <summary> Writes one template's files into the folder of each stack it belongs to (the SQL root once, however many stacks share it). </summary>
    private static async Task<IReadOnlyList<FileOutcome>> WriteForStacksAsync(GenerateOptions options, PlanStep step, List<(string RelativePath, string Content)> files, CancellationToken cancellationToken)
    {
        var config = step.Template.Config;
        var outcomes = new List<FileOutcome>();
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string stack in step.Stacks)
        {
            string root = config.OutputRoot.Equals("Stack", StringComparison.OrdinalIgnoreCase) ? stack : config.OutputRoot;
            string folder = Path.Combine(options.OutputDirectory, options.Project.OutputFolderOf(root), config.OutputFolderFor(stack));
            if (!written.Add(Path.GetFullPath(folder)))
                continue;
            outcomes.AddRange(await OutputWriter.WriteAsync(folder, files, dryRun: options.DryRun, withDiff: options.WithDiff, stack: root, cancellationToken: cancellationToken));
        }
        return outcomes;
    }
}
