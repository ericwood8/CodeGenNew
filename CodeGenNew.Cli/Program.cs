using System.Text.Json;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
        {
            ArgumentParser.PrintUsage(Console.Out);
            return args.Length == 0 ? 1 : 0;
        }

        CliOptions options;
        try
        {
            options = ArgumentParser.Parse(args);
        }
        catch (ArgumentParseException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.Error.WriteLine();
            ArgumentParser.PrintUsage(Console.Error);
            return 1;
        }

        string baseDirectory = AppContext.BaseDirectory;
        var settings = LoadSettings(Path.Combine(baseDirectory, "Settings.json"));

        string templatesDirectory = Path.Combine(baseDirectory, settings.TemplatesDirectory);
        string specialLogicColumnsConfigPath = Path.Combine(baseDirectory, settings.SpecialLogicColumnsConfigPath);
        string outputDirectory = options.OutputDirectory ?? Path.Combine(baseDirectory, settings.OutputDirectory);

        // Silently create Templates\/Output\ if missing and seed default templates/config from the
        // embedded copies baked into this exe -- never overwrites an existing (possibly customized)
        // file (Bugs3.txt items 5-7, 9).
        foreach (var notice in DefaultAssetSeeder.EnsureDefaultAssets(templatesDirectory, specialLogicColumnsConfigPath, outputDirectory, typeof(Program).Assembly)
                     .Where(n => n.Outcome != SeedOutcome.Created)) // creating a missing file stays silent
        {
            Console.WriteLine($"Note: {notice.Message}");
        }

        if (options.Provider != DatabaseProvider.SqlServer)
        {
            Console.Error.WriteLine($"Error: provider '{options.Provider}' is not implemented yet. Only SqlServer is supported in v1.");
            return 1;
        }

        var template = TemplateCatalog.FindByName(templatesDirectory, options.Template);
        if (template is null)
        {
            Console.Error.WriteLine($"Error: template '{options.Template}' was not found in '{templatesDirectory}'.");
            return 1;
        }

        string? password = options.Password;
        if (!options.Trusted && password is null)
            password = ConsolePasswordReader.Read($"Password for {options.UserName}@{options.Server}: ");

        var connectionRequest = new ConnectionRequest
        {
            Provider = DatabaseProvider.SqlServer,
            ServerName = options.Server,
            DatabaseName = options.Database,
            AuthMode = options.Trusted ? AuthMode.WindowsAuth : AuthMode.SqlLogin,
            UserName = options.UserName,
            Password = password
        };

        Console.WriteLine($"Connecting to {options.Server}\\{options.Database} ({(options.Trusted ? "Windows Auth" : "SQL Login")}) -- read-only schema lookup...");

        TableModel? model = null;
        var schemaReadOutcome = await RetryRunner.RunAsync("schema-read", async () =>
        {
            var schemaProvider = new SqlServerSchemaProvider(connectionRequest, specialLogicColumnsConfigPath);
            model = await schemaProvider.BuildTableModelAsync(
                options.Schema, options.Table, template.Config.NeedsRowData, template.Config.NeedsReferencedDisplayColumns);
            return $"Read schema for [{options.Schema}].[{options.Table}].";
        });

        if (!schemaReadOutcome.Success || model is null)
        {
            Console.Error.WriteLine(schemaReadOutcome.Message);
            return 1;
        }

        // One-time (cached in SpCanDeleteVerification.config), read-only, informational check -- see
        // Docs/specs.md section 7.1. Has no bearing on what gets generated; only reported when a fresh
        // (uncached) check actually ran, so a database already recorded as checked stays silent.
        string spCanDeleteConfigPath = Path.Combine(baseDirectory, settings.SpCanDeleteVerificationConfigPath);
        await using (var probeConnection = SqlServerConnectionFactory.CreateConnection(connectionRequest))
        {
            await probeConnection.OpenAsync();
            var (spCanDeleteStatus, wasCached) = await SpCanDeleteVerifier.GetOrVerifyAsync(
                probeConnection, options.Server, options.Database, spCanDeleteConfigPath);

            if (!wasCached)
            {
                Console.WriteLine(spCanDeleteStatus == SpCanDeleteStatus.Verified
                    ? $"spCanDelete verified on {options.Server}\\{options.Database} (recorded in {Path.GetFileName(spCanDeleteConfigPath)})."
                    : $"Note: spCanDelete was not found (or doesn't match the expected 2-parameter signature) on " +
                      $"{options.Server}\\{options.Database} (recorded in {Path.GetFileName(spCanDeleteConfigPath)}).");
            }
        }

        if (template.Config.RequiresPrimaryKey && !model.HasPrimaryKey)
        {
            Console.Error.WriteLine($"Error: template '{template.Name}' requires a primary key, but " +
                                     $"[{options.Schema}].[{options.Table}] doesn't have one.");
            return 1;
        }

        Console.WriteLine($"Generating '{template.Name}' for [{options.Schema}].[{options.Table}]...");
        var result = await TemplateRunner.RunAsync(template.FilePath, model);
        if (!result.Success)
        {
            Console.Error.WriteLine("Template generation failed:");
            foreach (string error in result.Errors)
                Console.Error.WriteLine($"  {error}");
            return 1;
        }

        List<string> writtenFiles;
        try
        {
            writtenFiles = await GeneratedFiles.WriteAsync(outputDirectory, template, model.TableName, result.GeneratedText!);
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"Template output could not be written: {ex.Message}");
            return 1;
        }
        foreach (string writtenFile in writtenFiles)
            Console.WriteLine($"Wrote {writtenFile}");
        Console.WriteLine("Done. codegen never modifies the target database or any other application -- " +
                           "review the generated file above and apply it yourself if you're happy with it.");
        return 0;
    }

    private static AppSettings LoadSettings(string settingsPath)
    {
        if (!File.Exists(settingsPath))
            return new AppSettings();

        string json = File.ReadAllText(settingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? new AppSettings();
    }
}
