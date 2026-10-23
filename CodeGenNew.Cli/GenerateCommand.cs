using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Cli;

/// <summary> <c>codegen generate</c> (every file of a project's stacks in one run) and <c>codegen essentials</c> (the files no table drives, no database needed). </summary>
public static class GenerateCommand
{
    public static async Task<int> RunAsync(CliOptions options, string baseDirectory, AppSettings settings, string templatesDirectory, string specialLogicColumnsConfigPath, string outputDirectory)
    {
        if (options.Command == "essentials" && options.List)
        {
            PrintEssentials(templatesDirectory);
            return ExitCodes.Success;
        }

        var project = ProjectSettings.None;
        if (options.Project is not null)
        {
            string projectsDirectory = options.ProjectsDirectory ?? Path.Combine(baseDirectory, settings.ProjectsDirectory);
            try { project = ProjectSettings.LoadNamed(projectsDirectory, options.Project); }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message} Create it, or list the existing ones in '{projectsDirectory}'.");
                return ExitCodes.Failure;
            }
        }
        if (options.ProjectOverrides.Count > 0)
            project = project.WithOverrides(options.ProjectOverrides);

        if (options.Command == "check-data")
            return await CheckDataAsync(options, project, specialLogicColumnsConfigPath);

        var stacks = ResolveStacks(options, project, out string? stackError);
        if (stackError is not null)
        {
            Console.Error.WriteLine($"Error: {stackError}");
            return ExitCodes.Failure;
        }

        return options.Command == "essentials"
            ? await EssentialsAsync(options, project, stacks, templatesDirectory, outputDirectory)
            : await GenerateAsync(options, project, stacks, templatesDirectory, specialLogicColumnsConfigPath, outputDirectory);
    }

    private static ISchemaProvider CreateProvider(CliOptions options, ProjectSettings project, string specialLogicColumnsConfigPath)
    {
        string? password = options.Password;
        if (!options.Trusted && password is null && options.Provider != DatabaseProvider.Sqlite)
            password = ConsolePasswordReader.Read($"Password for {options.UserName}@{options.Server}: ");
        var request = new ConnectionRequest
        {
            Provider = options.Provider, ServerName = options.Server, DatabaseName = options.Database,
            AuthMode = options.Trusted && options.Provider == DatabaseProvider.SqlServer ? AuthMode.WindowsAuth : AuthMode.SqlLogin,
            UserName = options.UserName, Password = password
        };
        return SchemaProviderFactory.Create(request, specialLogicColumnsConfigPath, project.Naming, project.Acronyms, project.IgnoredColumns, project.MoneyColumns, project.NotMoneyColumns);
    }

    /// <summary> <c>codegen check-data</c>: reads the rows of each table (a read-only SELECT, small tables only) and lists the stored values the generated validators would refuse, so a person can fix them or
    /// list the column in <c>PlainTextColumns</c> before a form blocks an unrelated edit. Exit code 1 when anything was found. </summary>
    private static async Task<int> CheckDataAsync(CliOptions options, ProjectSettings project, string specialLogicColumnsConfigPath)
    {
        if (ArgumentParser.MissingConnection(options) is { } missing)
        {
            Console.Error.WriteLine($"Error: {missing}");
            return ExitCodes.Failure;
        }

        var provider = CreateProvider(options, project, specialLogicColumnsConfigPath);
        var wanted = string.IsNullOrWhiteSpace(options.Table) ? null : options.Table.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var tables = (await provider.ListTablesAsync()).Where(t => t.SchemaName.EqualsIgnoreCase(options.Schema) && (wanted is null || wanted.Contains(t.TableName, StringComparer.OrdinalIgnoreCase))).ToList();

        int found = 0, checkedTables = 0;
        foreach (var summary in tables)
        {
            TableModel model;
            try { model = await provider.BuildTableModelAsync(summary.SchemaName, summary.TableName, includeRowData: true); }
            catch (Exception ex) when (ex.Message.Contains("more than", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Skipped    {summary.TableName}: too many rows to read ({ex.Message})");
                continue;
            }

            checkedTables++;
            foreach (var finding in DataCheck.Of(model, project))
            {
                found++;
                Console.WriteLine($"{finding.Table}.{finding.Column}: {finding.Count} of {finding.Total} value(s) must be {finding.Rule}, for example {string.Join(", ", finding.Examples.Select(e => "'" + e + "'"))}");
            }
        }

        Console.WriteLine(found == 0
            ? $"Checked {checkedTables} table(s): every stored value passes the rules."
            : $"Checked {checkedTables} table(s): {found} column(s) hold values a form would refuse. Fix them, or list the column in PlainTextColumns (a text shape) or remove the rule.");
        return found == 0 ? ExitCodes.Success : ExitCodes.Failure;
    }

    private static List<string> ResolveStacks(CliOptions options, ProjectSettings project, out string? error)
    {
        error = null;
        var named = options.Stacks.Count > 0 ? options.Stacks : project.Stacks.ToList();
        var stacks = new List<string>();
        foreach (string text in named)
        {
            string? stack = EssentialsCatalog.FindStack(text);
            if (stack is null)
            {
                error = $"Unknown stack '{text}'. The stacks are: {string.Join(", ", ProjectPlan.KnownStacks)}.";
                return [];
            }
            if (!stacks.Contains(stack, StringComparer.OrdinalIgnoreCase))
                stacks.Add(stack);
        }
        if (stacks.Count == 0)
            error = "No stack chosen: pass --stack (Api, WinUI3, React, Angular, Blazor, Uno, Rust, Python) or set Stacks in the project file.";
        return stacks;
    }

    private static void PrintEssentials(string templatesDirectory)
    {
        foreach (var (stack, menuText) in EssentialsCatalog.Stacks)
        {
            Console.WriteLine($"{menuText} (--stack {stack.ToLowerInvariant()}):");
            foreach (var group in EssentialsCatalog.Groups(templatesDirectory, stack))
                Console.WriteLine($"  {group.Name,-16} {group.Description}{(group.DefaultOn ? "" : "  (not ticked by default)")}");
        }
    }

    private static async Task<int> EssentialsAsync(CliOptions options, ProjectSettings project, List<string> stacks, string templatesDirectory, string outputDirectory)
    {
        var groups = new List<EssentialsGroup>();
        foreach (string stack in stacks)
        {
            var all = EssentialsCatalog.Groups(templatesDirectory, stack);
            if (options.Groups.Count > 0)
            {
                foreach (string wanted in options.Groups.Where(w => !all.Any(g => g.Name.EqualsIgnoreCase(w))))
                    Console.Error.WriteLine($"Warning: {stack} essentials have no group '{wanted}' (try --list).");
                all = all.Where(g => options.Groups.Contains(g.Name, StringComparer.OrdinalIgnoreCase)).ToList();
            }
            else
                all = all.Where(g => EssentialsCatalog.IsOnFor(g, project)).ToList();
            groups.AddRange(all);
        }
        if (groups.Count == 0)
        {
            Console.Error.WriteLine("Error: nothing to generate: no essentials group matches.");
            return ExitCodes.Failure;
        }

        Console.WriteLine($"Essentials: {string.Join(", ", groups.Select(g => $"{g.Stack}/{g.Name}"))}{(options.DryRun ? " (dry run)" : "")}");
        var run = await EssentialsCatalog.GenerateAsync(groups, project, outputDirectory, options.Replace, options.DryRun, options.Diff);
        PrintFiles(run.Outcomes, outputDirectory);
        PrintDiffs(run.Outcomes);
        foreach (string warning in run.Warnings)
            Console.WriteLine($"Warning: {warning}");
        foreach (var (group, errors) in run.Failures)
            Console.Error.WriteLine($"Error: {group.Stack}/{group.Name} failed: {string.Join(" | ", errors)}");
        if (!options.Replace && run.Outcomes.Any(o => o.Kind == FileOutcomeKind.Skipped))
            Console.WriteLine("Skipped files already exist and differ; they are hand-edited after the first generation. Pass --replace to overwrite them.");
        return run.Success ? 0 : 1;
    }

    private static async Task<int> GenerateAsync(CliOptions options, ProjectSettings project, List<string> stacks, string templatesDirectory, string specialLogicColumnsConfigPath, string outputDirectory)
    {
        if (ArgumentParser.MissingConnection(options) is { } missing)
        {
            Console.Error.WriteLine($"Error: {missing}");
            return ExitCodes.Failure;
        }

        var provider = CreateProvider(options, project, specialLogicColumnsConfigPath);

        Console.WriteLine($"Generating {string.Join(" + ", stacks)} for {project.ProjectName ?? options.Database}{(options.DryRun ? " (dry run)" : "")} into {outputDirectory}");
        var report = await ProjectGenerator.RunAsync(provider, templatesDirectory, new GenerateOptions
        {
            Project = project, Stacks = stacks, OutputDirectory = outputDirectory, DatabaseName = options.Database, Schema = options.Schema,
            DryRun = options.DryRun, Essentials = options.Essentials, EssentialsGroups = options.Groups.Count > 0 ? options.Groups : null, ReplaceEssentials = options.Replace,
            OnlyTemplates = options.Only.Count > 0 ? options.Only : null,
            Tables = string.IsNullOrWhiteSpace(options.Table) ? null : options.Table.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            DeleteStale = options.DeleteStale, WithDiff = options.Diff, Force = options.Force, WriteBeside = options.WriteBeside
        }, progress: null);

        foreach (string warning in report.Warnings)
            Console.WriteLine($"Warning: {warning}");
        PrintFiles(report.AllFiles.Where(f => f.Kind != FileOutcomeKind.Unchanged), outputDirectory);
        PrintDiffs(report.AllFiles);
        foreach (string refusal in report.Refusals)
            Console.WriteLine($"Skipped: {refusal}");
        foreach (var stale in report.Stale)
            Console.WriteLine(stale.Deleted ? $"Deleted    {stale.Path} (stale: no table or template produces it any more)"
                : $"Stale      {stale.Path} ({(stale.Edited ? "edited since it was generated, so it is left alone" : "no table or template produces it any more; --delete-stale removes it")})");
        if (report.Essentials is { } shown)
            foreach (string warning in shown.Warnings)
                Console.WriteLine($"Warning: {warning}");
        foreach (string error in report.Errors)
            Console.Error.WriteLine($"Error: {error}");
        if (report.Essentials is { } essentials)
            foreach (var (group, errors) in essentials.Failures)
                Console.Error.WriteLine($"Error: essentials {group.Stack}/{group.Name} failed: {string.Join(" | ", errors)}");

        Console.WriteLine($"Done: {report.Steps.Count} template runs, {report.Count(FileOutcomeKind.Created)} files created, {report.Count(FileOutcomeKind.Updated)} updated, " +
                          $"{report.Count(FileOutcomeKind.WouldWrite)} would be written, {report.Count(FileOutcomeKind.Unchanged)} unchanged, {report.Count(FileOutcomeKind.Skipped)} skipped, {report.Refusals.Count} refused.");
        int editedKept = report.Count(FileOutcomeKind.Edited), unknownKept = report.Count(FileOutcomeKind.Unknown);
        if (editedKept + unknownKept > 0)
            Console.WriteLine($"Kept {editedKept} edited and {unknownKept} unrecorded file(s) that differ from the new text; they were not overwritten. " +
                              (options.WriteBeside ? "The new text is beside each as .generated. " : "") + "Pass --force to overwrite them" + (options.WriteBeside ? "." : ", or --write-beside to keep them and write the new text next to them."));
        if (!report.ManifestWritten && !options.DryRun)
            Console.WriteLine("Note: the manifest was not written.");

        bool built = true;
        if ((options.Build || options.Test) && !options.DryRun)
        {
            var results = await ProjectBuilder.RunAsync(project, stacks, outputDirectory, options.Build, options.Test, message => Console.WriteLine($"  {message}"));
            foreach (var result in results)
            {
                Console.WriteLine($"{(result.Success ? "ok  " : "FAIL")} {result.Stack} {result.Step}: {result.Command}");
                foreach (string line in result.Summary.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    Console.WriteLine($"       {line}");
            }
            built = results.All(r => r.Success);
        }
        return report.Success && built ? 0 : 1;
    }

    private static void PrintDiffs(IEnumerable<FileOutcome> outcomes)
    {
        foreach (var outcome in outcomes.Where(o => o.Diff is not null))
        {
            Console.WriteLine($"--- {outcome.Kind}: {outcome.FullPath}");
            Console.Write(outcome.Diff);
        }
    }

    private static void PrintFiles(IEnumerable<FileOutcome> outcomes, string outputDirectory)
    {
        string root = Path.GetFullPath(outputDirectory);
        foreach (var outcome in outcomes)
        {
            string shown = outcome.FullPath.StartsWithIgnoreCase(root) ? outcome.FullPath[root.Length..].TrimStart('\\', '/') : outcome.FullPath;
            Console.WriteLine($"{outcome.Kind,-10} {shown}");
        }
    }
}
