using System.Collections.Concurrent;
using CodeGenNew.Core;
using Mono.TextTemplating;

namespace CodeGenNew.TemplateEngine;

/// <summary> Runs templates like <see cref="TemplateRunner"/> but compiles each template file only once: a whole-project generation runs the same template for every table, and the T4 compile
/// (about a second each, in a compiler process of its own) is what the time goes on. The compiled class reads its parameters (<c>Model</c> / <c>Database</c> and <c>Project</c>) from the session each time it runs, so
/// one compile serves every table. <see cref="Prepare"/> compiles a list of templates on the other cores while the caller already runs the first ones. A template file edited after it was compiled
/// is not picked up until a new cache is made (one cache lives for one run). Running one template is not thread safe (it uses the compiled template's session); compiling is. </summary>
public sealed class TemplateCache : IDisposable
{
    private sealed class Entry
    {
        public required TemplateGenerator Generator { get; init; }
        public CompiledTemplate? Compiled { get; init; }
        public IReadOnlyList<string> CompileErrors { get; init; } = [];
    }

    private readonly ConcurrentDictionary<string, Lazy<Task<Entry>>> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _stopPreparing = new();
    private readonly CompiledTemplateStore? _store;
    private int _compilerRuns;

    /// <summary> How many templates this cache sent to the compiler (those read back from disk do not count). </summary>
    public int CompilerRuns => _compilerRuns;

    /// <summary> A cache that also keeps compiled templates on disk (see <see cref="CompiledTemplateStore"/>), by default under the user's local application data;
    /// the environment variable CODEGENNEW_TEMPLATE_CACHE names another folder or says "off". </summary>
    public TemplateCache() : this(CompiledTemplateStore.Default())
    {
    }

    private TemplateCache(CompiledTemplateStore? store) => _store = store;

    /// <summary> A cache whose compiled templates are kept in this folder. </summary>
    public static TemplateCache InFolder(string folder) => new(CompiledTemplateStore.At(folder));

    /// <summary> A cache that compiles every template itself and keeps nothing on disk. </summary>
    public static TemplateCache WithoutDisk() => new((CompiledTemplateStore?)null);

    public Task<TemplateResult> RunAsync(string templateFilePath, TableModel model, ProjectSettings? project = null, CancellationToken cancellationToken = default) =>
        RunCoreAsync(templateFilePath, "Model", model, project, cancellationToken);

    public Task<TemplateResult> RunAsync(string templateFilePath, DatabaseModel database, ProjectSettings? project = null, CancellationToken cancellationToken = default) =>
        RunCoreAsync(templateFilePath, "Database", database, project, cancellationToken);

    public Task<TemplateResult> RunAsync(string templateFilePath, ProjectSettings project, CancellationToken cancellationToken = default) =>
        RunCoreAsync(templateFilePath, null, null, project, cancellationToken);

    /// <summary> Starts compiling these templates in the background, a few at a time, so that a later run finds them ready. A template asked for before its turn is compiled at once. </summary>
    public void Prepare(IEnumerable<string> templateFilePaths)
    {
        var paths = templateFilePaths.ToList();
        var token = _stopPreparing.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2), CancellationToken = token };
                await Parallel.ForEachAsync(paths, options, async (path, _) => await EntryFor(path));
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private Task<Entry> EntryFor(string path) => _entries.GetOrAdd(path, p => new Lazy<Task<Entry>>(() => Task.Run(() => CompileAsync(p)))).Value;

    private async Task<Entry> CompileAsync(string path)
    {
        var generator = new TemplateGenerator();
        generator.Refs.Add(typeof(TableModel).Assembly.Location);
        string text = await File.ReadAllTextAsync(path);
        string? key = _store is null ? null : CompiledTemplateStore.KeyOf(text);
        if (key is not null && _store!.TryLoad(generator, key) is { } stored)
            return new Entry { Generator = generator, Compiled = stored };

        Interlocked.Increment(ref _compilerRuns);
        var compiled = await generator.CompileTemplateAsync(text);
        var errors = generator.Errors.Cast<System.CodeDom.Compiler.CompilerError>().Select(e => e.ToString()).ToList();
        if (generator.Errors.HasErrors)
            return new Entry { Generator = generator, CompileErrors = errors };
        if (key is not null)
            _store!.Save(compiled, key);
        return new Entry { Generator = generator, Compiled = compiled, CompileErrors = errors };
    }

    private async Task<TemplateResult> RunCoreAsync(string path, string? parameterName, object? model, ProjectSettings? project, CancellationToken cancellationToken)
    {
        var entry = await EntryFor(path).WaitAsync(cancellationToken);

        if (entry.Compiled is null)
            return new TemplateResult { Success = false, Errors = entry.CompileErrors };

        var session = entry.Generator.GetOrCreateSession();
        if (parameterName is not null)
            session[parameterName] = model;
        // the project decides which column is the user table's password hash; every template then sees it as a credential
        if (model is TableModel markTable)
            UserTable.MarkCredentials(markTable, project ?? ProjectSettings.None);
        else if (model is DatabaseModel markDatabase)
            UserTable.MarkCredentials(markDatabase, project ?? ProjectSettings.None);
        session["Project"] = project ?? ProjectSettings.None;

        entry.Generator.Errors.Clear();
        string text;
        try
        {
            text = entry.Compiled.Process();
        }
        finally
        {
            // the session would otherwise keep the last table or database (with its rows) alive for as long as the cache lives
            if (parameterName is not null)
                session.Remove(parameterName);
        }
        var runErrors = entry.Generator.Errors.Cast<System.CodeDom.Compiler.CompilerError>().Select(e => e.ToString()).ToList();
        return entry.Generator.Errors.HasErrors
            ? new TemplateResult { Success = false, Errors = runErrors }
            : new TemplateResult { Success = true, GeneratedText = text, Errors = runErrors };
    }

    public void Dispose()
    {
        _stopPreparing.Cancel();
        foreach (var lazy in _entries.Values)
            if (lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully)
                lazy.Value.Result.Compiled?.Dispose();
        _entries.Clear();
        _stopPreparing.Dispose();
    }
}
