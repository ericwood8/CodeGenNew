using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Generation;

public sealed class GenerateOptions
{
    public required ProjectSettings Project { get; init; }
    /// <summary> The stacks to generate (Api, WinUI3, React, Angular). </summary>
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
    /// <summary> Run only these templates (by name, <c>SP_Search</c> or <c>CS_Entity_v1.tt</c>); null = the whole plan. </summary>
    public IReadOnlyList<string>? OnlyTemplates { get; init; }
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

    public IEnumerable<FileOutcome> AllFiles => Steps.SelectMany(s => s.Files).Concat(Essentials?.Outcomes ?? []);
    public int Count(FileOutcomeKind kind) => AllFiles.Count(f => f.Kind == kind);
    public bool Success => Errors.Count == 0 && (Essentials?.Success ?? true);
}

/// <summary> Generates every file of a project in one process: the schema is read once, the templates and tables come from <see cref="ProjectPlan"/>, and files whose text did not change are
/// left alone. This is what each sample's Regenerate.sh did with one command-line run per file. </summary>
public static class ProjectGenerator
{
    public static async Task<GenerateReport> RunAsync(ISchemaProvider provider, string templatesDirectory, GenerateOptions options, Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var report = new GenerateReport();
        var project = options.Project;
        using var cache = new TemplateCache();

        progress?.Invoke("Reading the schema...");
        var database = await provider.BuildAsync(options.DatabaseName, options.Schema, cancellationToken);
        progress?.Invoke($"Read {database.Tables.Count} tables of [{options.Schema}].");

        foreach (var table in database.Tables)
            foreach (var column in table.UnsupportedColumns)
                report.Warnings.Add($"column {table.DbTableName}.{column.DbName} has the type '{column.SqlTypeDeclaration}', which CodeGenNew does not map; list it in IgnoredColumns to leave it out.");

        var templates = TemplateCatalog.Discover(templatesDirectory);
        var steps = ProjectPlan.Build(templates, database, project, options.Stacks);
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

        if (options.Essentials)
        {
            var groups = options.Stacks.SelectMany(s => EssentialsCatalog.Groups(templatesDirectory, s)).ToList();
            if (options.EssentialsGroups is { Count: > 0 } named)
                groups = groups.Where(g => named.Contains(g.Name, StringComparer.OrdinalIgnoreCase)).ToList();
            else
                groups = groups.Where(g => g.DefaultOn).ToList();
            progress?.Invoke("Essentials...");
            report.Essentials = await EssentialsCatalog.GenerateAsync(groups, project, options.OutputDirectory, options.ReplaceEssentials, options.DryRun, cancellationToken);
        }
        return report;
    }

    /// <summary> Writes one template's files into the folder of each stack it belongs to (the SQL root once, however many stacks share it). </summary>
    private static async Task<IReadOnlyList<FileOutcome>> WriteForStacksAsync(GenerateOptions options, PlanStep step, List<(string RelativePath, string Content)> files, CancellationToken cancellationToken)
    {
        var config = step.Template.Config;
        var outcomes = new List<FileOutcome>();
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string stack in step.Stacks)
        {
            string root = config.OutputRoot.Equals("Sql", StringComparison.OrdinalIgnoreCase) ? "Sql" : stack;
            string folder = Path.Combine(options.OutputDirectory, options.Project.OutputFolderOf(root), config.OutputFolderFor(stack));
            if (!written.Add(Path.GetFullPath(folder)))
                continue;
            outcomes.AddRange(await OutputWriter.WriteAsync(folder, files, dryRun: options.DryRun, cancellationToken: cancellationToken));
        }
        return outcomes;
    }
}
