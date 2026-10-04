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

        string baseDirectory = AppHome.Resolve(AppContext.BaseDirectory);   // the program's folder, or the user's own folder for an installed tool
        Directory.CreateDirectory(baseDirectory);
        var settings = AppSettings.Load(Path.Combine(baseDirectory, "Settings.json"));

        string templatesDirectory = Path.Combine(baseDirectory, settings.TemplatesDirectory);
        string specialLogicColumnsConfigPath = Path.Combine(baseDirectory, settings.SpecialLogicColumnsConfigPath);
        string outputDirectory = options.OutputDirectory ?? Path.Combine(baseDirectory, settings.OutputDirectory);

        // Silently create Templates\/Output\ if missing and seed default templates/config from the
        // embedded copies baked into this exe -- never overwrites an existing (possibly customized)
        // file.
        foreach (var notice in DefaultAssetSeeder.EnsureDefaultAssets(templatesDirectory, specialLogicColumnsConfigPath, outputDirectory, typeof(Program).Assembly)
                     .Where(n => n.Outcome != SeedOutcome.Created)) // creating a missing file stays silent
        {
            Console.WriteLine($"Note: {notice.Message}");
        }


        if (options.Command is not null)
            return await GenerateCommand.RunAsync(options, baseDirectory, settings, templatesDirectory, specialLogicColumnsConfigPath, outputDirectory);

        var template = TemplateCatalog.FindByName(templatesDirectory, options.Template);
        if (template is null)
        {
            Console.Error.WriteLine($"Error: template '{options.Template}' was not found in '{templatesDirectory}'.");
            return 1;
        }

        // The project is read first: its NamingStyle decides the names the schema reader gives tables and columns.
        ProjectSettings project = ProjectSettings.None;
        if (options.Project is not null)
        {
            string projectsDirectory = options.ProjectsDirectory ?? Path.Combine(baseDirectory, settings.ProjectsDirectory);
            try
            {
                project = ProjectSettings.LoadNamed(projectsDirectory, options.Project);
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message} Create it, or list the existing ones in '{projectsDirectory}'.");
                return 1;
            }
        }
        if (options.ProjectOverrides.Count > 0)
            project = project.WithOverrides(options.ProjectOverrides);

        if (template.Config.NoDatabase)
            return await GenerateNoDatabaseAsync(template, project, outputDirectory);

        if (ArgumentParser.MissingConnection(options) is { } missing)
        {
            Console.Error.WriteLine($"Error: {missing}");
            Console.Error.WriteLine();
            ArgumentParser.PrintUsage(Console.Error);
            return 1;
        }

        string? password = options.Password;
        if (!options.Trusted && password is null)
            password = ConsolePasswordReader.Read($"Password for {options.UserName}@{options.Server}: ");

        var connectionRequest = new ConnectionRequest
        {
            Provider = options.Provider,
            ServerName = options.Server,
            DatabaseName = options.Database,
            AuthMode = options.Trusted && options.Provider == DatabaseProvider.SqlServer ? AuthMode.WindowsAuth : AuthMode.SqlLogin,
            UserName = options.UserName,
            Password = password
        };

        Console.WriteLine($"Connecting to {options.Server}\\{options.Database} ({(options.Provider == DatabaseProvider.PostgreSql ? "PostgreSQL" : options.Provider == DatabaseProvider.MySql ? "MySQL" : options.Trusted ? "Windows Auth" : "SQL Login")}) -- read-only schema lookup...");

        if (!template.Config.DatabaseOnly && string.IsNullOrWhiteSpace(options.Table))
        {
            Console.Error.WriteLine($"Error: -t/--table is required for template '{template.Name}'.");
            return 1;
        }

        TableModel? model = null;
        DatabaseModel? database = null;
        var schemaReadOutcome = await RetryRunner.RunAsync("schema-read", async () =>
        {
            var schemaProvider = SchemaProviderFactory.Create(connectionRequest, specialLogicColumnsConfigPath, project.Naming, project.Acronyms, project.IgnoredColumns);
            if (template.Config.DatabaseOnly)
            {
                database = await schemaProvider.BuildAsync(options.Database, options.Schema);
                return $"Read schema for {database.Tables.Count} tables of [{options.Schema}].";
            }
            model = await schemaProvider.BuildTableModelAsync(
                options.Schema, options.Table!, template.Config.NeedsRowData, template.Config.NeedsReferencedDisplayColumns);
            return $"Read schema for [{options.Schema}].[{options.Table}].";
        });

        if (!schemaReadOutcome.Success || (model is null && database is null))
        {
            Console.Error.WriteLine(schemaReadOutcome.Message);
            return 1;
        }

        // spCanDelete is a SQL Server stored procedure of the author's own tooling; PostgreSQL and MySQL have no such check.
        if (options.Provider == DatabaseProvider.SqlServer)
        {
            // One-time (cached in SpCanDeleteVerification.config), read-only, informational check -- see
            // Docs/specs.md section 7.1. Has no bearing on what gets generated; only reported when a fresh
            // (uncached) check actually ran, so a database already recorded as checked stays silent.
            string spCanDeleteConfigPath = Path.Combine(baseDirectory, settings.SpCanDeleteVerificationConfigPath);
            await using (var probeConnection = connectionRequest.CreateConnection())
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
        }

        foreach (var table in database?.Tables ?? (model is null ? [] : [model]))
        {
            foreach (var column in table.UnsupportedColumns)
                Console.WriteLine($"Warning: column {table.DbTableName}.{column.DbName} has the type '{column.SqlTypeDeclaration}', which CodeGenNew does not map; the generated code cannot use it. " +
                                  $"List it in the project setting IgnoredColumns ({table.DbTableName}.{column.DbName}) to leave it out.");
        }

        if (database is not null)
            return await GenerateDatabaseAsync(template, database, project, outputDirectory);

        string? refusal = template.Config.Refuse(model!);
        if (refusal is not null)
        {
            Console.Error.WriteLine($"Error: template '{template.Name}' can't be used for [{options.Schema}].[{options.Table}]: {refusal}");
            return 1;
        }

        Console.WriteLine($"Generating '{template.Name}' for [{options.Schema}].[{options.Table}]...");
        var result = await TemplateRunner.RunAsync(template.FilePath, model!, project);
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
            writtenFiles = await GeneratedFiles.WriteAsync(outputDirectory, template, model!.TableName, result.GeneratedText!);
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

    /// <summary> A database-level template (DbContext, API registration): one run over every table of the schema. The file is named after the
    /// project's context ("InvoiceSystemContext.cs"), or after the database when no project is chosen. </summary>
    private static async Task<int> GenerateNoDatabaseAsync(TemplateInfo template, ProjectSettings project, string outputDirectory)
    {
        Console.WriteLine($"Generating '{template.Name}' (no database needed)...");
        var result = await TemplateRunner.RunAsync(template.FilePath, project);
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
            writtenFiles = await GeneratedFiles.WriteAsync(outputDirectory, template, project.ProjectName ?? "Project", result.GeneratedText!);
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"Template output could not be written: {ex.Message}");
            return 1;
        }
        foreach (string writtenFile in writtenFiles)
            Console.WriteLine($"Wrote {writtenFile}");
        Console.WriteLine("Done. review the generated files and add them to your project yourself.");
        return 0;
    }

    private static async Task<int> GenerateDatabaseAsync(TemplateInfo template, DatabaseModel database, ProjectSettings project, string outputDirectory)
    {
        Console.WriteLine($"Generating '{template.Name}' for {database.Tables.Count} tables of [{database.SchemaName}]...");
        var result = await TemplateRunner.RunAsync(template.FilePath, database, project);
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
            writtenFiles = await GeneratedFiles.WriteAsync(outputDirectory, template, project.ContextName ?? database.DatabaseName + "Context", result.GeneratedText!);
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"Template output could not be written: {ex.Message}");
            return 1;
        }
        foreach (string writtenFile in writtenFiles)
            Console.WriteLine($"Wrote {writtenFile}");
        Console.WriteLine("Done. codegen never modifies the target database or any other application -- review the generated file and apply it yourself.");
        return 0;
    }
}
