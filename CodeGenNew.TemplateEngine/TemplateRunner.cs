using CodeGenNew.Core;
using Mono.TextTemplating;

namespace CodeGenNew.TemplateEngine;

public class TemplateResult
{
    public bool Success { get; init; }
    public string? GeneratedText { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>
/// Runs a .tt template in-process via Mono.TextTemplating, passing a TableModel in as a T4 parameter
/// through the session dictionary (Docs/specs.md section 8, section 15.3). No intermediate file, no
/// caching/preprocessing -- loads and transforms the .tt file fresh from disk on every call, so editing
/// a template takes effect on the very next generation.
///
/// This needs a real .NET SDK on the machine: compiling the generated template class shells out to a
/// "dotnet"-hosted csc, located by walking a fixed number of parent directories up from the current
/// runtime's own directory. That's correct for a normal framework-dependent app (whose runtime directory
/// is the shared framework under Program Files\dotnet\shared\...) but breaks for a self-contained
/// deployment (whose "runtime directory" is just its own output folder), producing a nonsensical path and
/// failing with "The system cannot find the file specified." (Bugs2.txt item 5) -- which is why
/// CodeGenNew.App is framework-dependent (SelfContained=false in its csproj) even though the Windows App
/// SDK's own native runtime is still bundled (WindowsAppSDKSelfContained=true). Fine for "a developer's
/// own box" (README) -- that box already has the .NET SDK installed, or this project wouldn't build.
/// </summary>
public static class TemplateRunner
{
    public static async Task<TemplateResult> RunAsync(string templateFilePath, TableModel model, CancellationToken cancellationToken = default)
    {
        var generator = new TemplateGenerator();

        // Make CodeGenNew.Core (and therefore TableModel/ColumnModel/ForeignKeyModel) resolvable
        // when the T4 engine compiles the generated template class.
        generator.Refs.Add(typeof(TableModel).Assembly.Location);

        var session = generator.GetOrCreateSession();
        session["Model"] = model;

        string tempOutputFile = Path.GetTempFileName();
        try
        {
            bool success = await generator.ProcessTemplateAsync(templateFilePath, tempOutputFile, cancellationToken);

            var errors = generator.Errors.Cast<System.CodeDom.Compiler.CompilerError>()
                .Select(e => e.ToString())
                .ToList();

            if (!success)
                return new TemplateResult { Success = false, Errors = errors };

            string generatedText = await File.ReadAllTextAsync(tempOutputFile, cancellationToken);
            return new TemplateResult { Success = true, GeneratedText = generatedText, Errors = errors };
        }
        finally
        {
            if (File.Exists(tempOutputFile))
                File.Delete(tempOutputFile);
        }
    }
}
