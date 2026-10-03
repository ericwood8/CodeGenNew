using CodeGenNew.Core;
using Mono.TextTemplating;

namespace CodeGenNew.TemplateEngine;

/// <summary> Runs templates like <see cref="TemplateRunner"/> but compiles each template file only once: a whole-project generation runs the same template for every table, and the T4 compile
/// (about a second each) is what the time goes on. The compiled class reads its parameters (<c>Model</c> / <c>Database</c> and <c>Project</c>) from the session each time it runs, so
/// one compile serves every table. A template file edited after it was compiled is not picked up until a new cache is made (one cache lives for one run). </summary>
public sealed class TemplateCache : IDisposable
{
    private sealed class Entry
    {
        public required TemplateGenerator Generator { get; init; }
        public CompiledTemplate? Compiled { get; init; }
        public IReadOnlyList<string> CompileErrors { get; init; } = [];
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public Task<TemplateResult> RunAsync(string templateFilePath, TableModel model, ProjectSettings? project = null, CancellationToken cancellationToken = default) =>
        RunCoreAsync(templateFilePath, "Model", model, project, cancellationToken);

    public Task<TemplateResult> RunAsync(string templateFilePath, DatabaseModel database, ProjectSettings? project = null, CancellationToken cancellationToken = default) =>
        RunCoreAsync(templateFilePath, "Database", database, project, cancellationToken);

    public Task<TemplateResult> RunAsync(string templateFilePath, ProjectSettings project, CancellationToken cancellationToken = default) =>
        RunCoreAsync(templateFilePath, null, null, project, cancellationToken);

    private async Task<TemplateResult> RunCoreAsync(string path, string? parameterName, object? model, ProjectSettings? project, CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(path, out var entry))
        {
            var generator = new TemplateGenerator();
            generator.Refs.Add(typeof(TableModel).Assembly.Location);
            var compiled = await generator.CompileTemplateAsync(await File.ReadAllTextAsync(path, cancellationToken), cancellationToken);
            var errors = generator.Errors.Cast<System.CodeDom.Compiler.CompilerError>().Select(e => e.ToString()).ToList();
            entry = new Entry { Generator = generator, Compiled = generator.Errors.HasErrors ? null : compiled, CompileErrors = errors };
            _entries[path] = entry;
        }

        if (entry.Compiled is null)
            return new TemplateResult { Success = false, Errors = entry.CompileErrors };

        var session = entry.Generator.GetOrCreateSession();
        if (parameterName is not null)
            session[parameterName] = model;
        session["Project"] = project ?? ProjectSettings.None;

        entry.Generator.Errors.Clear();
        string text = entry.Compiled.Process();
        var runErrors = entry.Generator.Errors.Cast<System.CodeDom.Compiler.CompilerError>().Select(e => e.ToString()).ToList();
        return entry.Generator.Errors.HasErrors
            ? new TemplateResult { Success = false, Errors = runErrors }
            : new TemplateResult { Success = true, GeneratedText = text, Errors = runErrors };
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values)
            entry.Compiled?.Dispose();
        _entries.Clear();
    }
}
