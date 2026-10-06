using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using CodeGenNew.Core;

namespace CodeGenNew.Generation;

/// <summary> One build or test command of one stack and how it went. <see cref="Summary"/> is the few lines worth reading (the errors, the test totals), not the whole output. </summary>
public sealed record StepResult(string Stack, string Step, string Command, int ExitCode, string Summary)
{
    public bool Success => ExitCode == 0;
}

/// <summary> Builds and tests the stacks of a generated project after a generate (<c>--build</c>, <c>--test</c>): the same commands each sample's Regenerate.sh ran. The default command of a stack is
/// in <see cref="DefaultCommand"/>; a project overrides it with <c>BuildApi</c>, <c>TestReact</c> ... (a blank value, <c>none</c>, skips the step). </summary>
public static class ProjectBuilder
{
    public static string? DefaultCommand(string stack, string step) => (stack.ToLowerInvariant(), step) switch
    {
        ("api", "build") => "dotnet build -v q",
        ("winui3", "build") => "dotnet build -v q",
        ("react", "build") => "npm run build",
        ("react", "test") => "npm test",
        ("angular", "build") => "npm run build",
        ("angular", "test") => "npm test -- --watch=false",
        ("blazor", "build") => "dotnet build -v q",
        ("python", "build") => "python -m compileall -q app",
        ("rust", "build") => "cargo check",
        ("rust", "test") => "cargo test",
        _ => null
    };

    /// <summary> The command to run for a step, or null when there is none (a stack without tests, or a step the project switched off). </summary>
    public static string? CommandFor(ProjectSettings project, string stack, string step)
    {
        string? custom = project.StepCommand(step, stack);
        if (custom is not null)
            return custom.Length == 0 || custom.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : custom;
        if (step == "test" && stack.Equals("Api", StringComparison.OrdinalIgnoreCase) && project.ApiTests)
            return $"dotnet test \"{Path.GetRelativePath(project.OutputFolderOf("api"), project.OutputFolderOf("apitests"))}\" -v q";
        return DefaultCommand(stack, step);
    }

    public static async Task<List<StepResult>> RunAsync(ProjectSettings project, IReadOnlyList<string> stacks, string outputDirectory, bool build, bool test,
        Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var results = new List<StepResult>();
        foreach (string stack in stacks)
        {
            string folder = Path.GetFullPath(Path.Combine(outputDirectory, project.OutputFolderOf(stack)));
            if (!Directory.Exists(folder))
            {
                results.Add(new StepResult(stack, "build", "", 1, $"The folder {folder} does not exist, so there is nothing to build."));
                continue;
            }

            bool web = stack.Equals("React", StringComparison.OrdinalIgnoreCase) || stack.Equals("Angular", StringComparison.OrdinalIgnoreCase);
            if (web && (build || test) && !Directory.Exists(Path.Combine(folder, "node_modules")))
            {
                progress?.Invoke($"{stack}: npm install");
                var install = await RunCommandAsync(stack, "install", "npm install --no-audit --no-fund", folder, cancellationToken);
                results.Add(install);
                if (!install.Success)
                    continue;
            }

            foreach (string step in new[] { "build", "test" }.Where(s => s == "build" ? build : test))
            {
                string? command = CommandFor(project, stack, step);
                if (command is null)
                    continue;
                progress?.Invoke($"{stack}: {command}");
                var result = await RunCommandAsync(stack, step, command, folder, cancellationToken);
                // a WinUI 3 build after a column or control changed can fail on stale generated *.g.cs files in obj: clear it and build again (the second build is the one that counts)
                if (!result.Success && step == "build" && stack.Equals("WinUI3", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(folder, "obj")))
                {
                    progress?.Invoke($"{stack}: clearing obj and building again");
                    Directory.Delete(Path.Combine(folder, "obj"), recursive: true);
                    await RunCommandAsync(stack, "restore", "dotnet restore -v q", folder, cancellationToken);
                    result = await RunCommandAsync(stack, step, command, folder, cancellationToken);
                    if (!result.Success)
                        result = await RunCommandAsync(stack, step, command, folder, cancellationToken);
                }
                results.Add(result);
            }
        }
        return results;
    }

    private static async Task<StepResult> RunCommandAsync(string stack, string step, string command, string folder, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            Arguments = (OperatingSystem.IsWindows() ? "/c " : "-c ") + (OperatingSystem.IsWindows() ? command : "\"" + command.Replace("\"", "\\\"") + "\""),
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(info)!;
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        process.WaitForExit();   // the redirected streams are complete after this
        return new StepResult(stack, step, command, process.ExitCode, Summarise(output.ToString(), process.ExitCode));
    }

    /// <summary> The lines worth showing: compiler and test errors and the totals; the last lines when none match. </summary>
    public static string Summarise(string output, int exitCode)
    {
        var lines = output.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var interesting = lines.Where(l => Regex.IsMatch(l, @"\berror\b|\bFAIL|Build succeeded|Tests\s+\d|Test Files|Application bundle|built in|Passed!|Failed!", RegexOptions.IgnoreCase)).Distinct().Take(12).ToList();
        if (interesting.Count == 0)
            interesting = lines.TakeLast(exitCode == 0 ? 2 : 8).ToList();
        return string.Join("\n", interesting.Select(l => l.Length > 240 ? l[..240] + "..." : l));
    }
}
