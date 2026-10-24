using CodeGenNew.Connections;
using CodeGenNew.Core;

namespace CodeGenNew.Cli;

public class ArgumentParseException(string message) : Exception(message);

public static class ArgumentParser
{
    public static CliOptions Parse(string[] args)
    {
        string? server = null, database = null, schema = null, table = null, template = null;
        string? outputDirectory = null, userName = null, password = null, project = null, projectsDirectory = null;
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool trusted = false;
        var provider = DatabaseProvider.SqlServer;

        string? command = null;
        var stacks = new List<string>();
        var groups = new List<string>();
        var only = new List<string>();
        StrategyUi? ui = null;
        StrategyAccess? access = null;
        StrategyStyle? apiStyle = null;
        StrategyLanguage? apiLanguage = null;
        bool essentials = false, replace = false, dryRun = false, list = false, deleteStale = false, build = false, test = false, diff = false, force = false, writeBeside = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (i == 0 && arg is "generate" or "essentials" or "check-data")
            {
                command = arg;
                continue;
            }

            string Value()
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentParseException($"Missing value for '{arg}'.");
                return args[++i];
            }

            switch (arg)
            {
                case "-S": case "--server": server = Value(); break;
                case "-d": case "--database": database = Value(); break;
                case "-s": case "--schema": schema = Value(); break;
                case "-t": case "--table": table = Value(); break;
                case "-T": case "--template": template = Value(); break;
                case "-o": case "--output": outputDirectory = Value(); break;
                case "-U": case "--user": userName = Value(); break;
                case "-P": case "--password": password = Value(); break;
                case "-E": case "--trusted": trusted = true; break;
                case "--project": project = Value(); break;
                case "--stack": stacks.AddRange(Value().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)); break;
                case "--ui":
                    string uiText = Value();
                    ui = StrategyRules.TryParseUi(uiText, out var parsedUi) ? parsedUi : throw new ArgumentParseException($"Unknown --ui '{uiText}'. Valid values: {string.Join(", ", StrategyRules.Uis)}.");
                    break;
                case "--access-mode":
                    string accessText = Value();
                    access = StrategyRules.TryParseAccess(accessText, out var parsedAccess) ? parsedAccess : throw new ArgumentParseException($"Unknown --access-mode '{accessText}'. Valid values: Routines, Ef, OwnSql.");
                    break;
                case "--api-style":
                    string styleText = Value();
                    apiStyle = StrategyRules.TryParseStyle(styleText, out var parsedStyle) ? parsedStyle : throw new ArgumentParseException($"Unknown --api-style '{styleText}'. Valid values: Rest, OData.");
                    break;
                case "--api-language":
                    string languageText = Value();
                    apiLanguage = StrategyRules.TryParseLanguage(languageText, out var parsedLanguage) ? parsedLanguage : throw new ArgumentParseException($"Unknown --api-language '{languageText}'. Valid values: CSharp, Rust, Python.");
                    break;
                case "--groups": groups.AddRange(Value().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)); break;
                case "--only": only.AddRange(Value().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)); break;
                case "--essentials": essentials = true; break;
                case "--replace": replace = true; break;
                case "--dry-run": dryRun = true; break;
                case "--list": list = true; break;
                case "--delete-stale": deleteStale = true; break;
                case "--build": build = true; break;
                case "--test": test = true; break;
                case "--diff": diff = true; break;
                case "--force": force = true; break;
                case "--write-beside": writeBeside = true; break;
                case "--projects-dir": projectsDirectory = Value(); break;
                case "--set":
                    string setting = Value();
                    int equals = setting.IndexOf('=');
                    if (equals <= 0)
                        throw new ArgumentParseException($"--set takes Key=Value, not '{setting}'.");
                    string key = ProjectSettings.Keys.FirstOrDefault(k => k.EqualsIgnoreCase(setting[..equals].Trim()))
                        ?? throw new ArgumentParseException($"Unknown project setting '{setting[..equals].Trim()}' in --set. Known keys: {string.Join(", ", ProjectSettings.Keys)}.");
                    overrides[key] = setting[(equals + 1)..];
                    break;
                case "--project-name": overrides["ProjectName"] = Value(); break;
                case "--view-ns": overrides["ViewNamespace"] = Value(); break;
                case "--viewmodel-ns": overrides["ViewModelNamespace"] = Value(); break;
                case "--context": overrides["ContextName"] = Value(); break;
                case "--context-ns": overrides["ContextNamespace"] = Value(); break;
                case "--api-ns": overrides["ApiNamespace"] = Value(); break;
                case "--enum-ns": overrides["EnumNamespace"] = Value(); break;
                case "--repo-ns": overrides["RepoNamespace"] = Value(); break;
                case "--entity-ns": overrides["EntityNamespace"] = Value(); break;
                case "--naming": overrides["NamingStyle"] = Value(); break;
                case "--acronyms": overrides["Acronyms"] = Value(); break;
                case "--min-year": overrides["MinYear"] = Value(); break;
                case "--max-year": overrides["MaxYear"] = Value(); break;
                case "--provider":
                    string providerText = Value();
                    if (!Enum.TryParse(providerText, ignoreCase: true, out provider))
                        throw new ArgumentParseException($"Unknown --provider '{providerText}'. Valid values: SqlServer, PostgreSql, MySql, Sqlite.");
                    break;
                default:
                    throw new ArgumentParseException($"Unrecognized argument: '{arg}'.");
            }
        }

        if (command is null && string.IsNullOrWhiteSpace(template)) throw new ArgumentParseException("-T/--template is required.");

        foreach (string yearKey in new[] { "MinYear", "MaxYear" })
        {
            if (overrides.TryGetValue(yearKey, out string? yearText) && !int.TryParse(yearText, out _))
                throw new ArgumentParseException($"--{(yearKey == "MinYear" ? "min" : "max")}-year must be a whole number, not '{yearText}'.");
        }

        var options = new CliOptions
        {
            Provider = provider,
            Server = server ?? "",
            Database = database ?? "",
            Schema = schema ?? (provider == DatabaseProvider.PostgreSql ? "public" : provider == DatabaseProvider.MySql ? database ?? "" : provider == DatabaseProvider.Sqlite ? "main" : "dbo"),
            Table = table,
            Template = template ?? "",
            Command = command,
            Essentials = essentials,
            Replace = replace,
            DryRun = dryRun,
            List = list,
            DeleteStale = deleteStale,
            Build = build,
            Test = test,
            Diff = diff,
            Force = force,
            WriteBeside = writeBeside,
            OutputDirectory = outputDirectory,
            Trusted = trusted,
            UserName = userName,
            Password = password,
            Project = project,
            ProjectsDirectory = projectsDirectory,
            Ui = ui,
            Access = access,
            ApiStyle = apiStyle,
            ApiLanguage = apiLanguage
        };
        foreach (var (key, value) in overrides)
            options.ProjectOverrides[key] = value;
        options.Stacks.AddRange(stacks);
        options.Groups.AddRange(groups);
        options.Only.AddRange(only);
        return options;
    }

    /// <summary> What is missing for a template that reads a database (every template except one with NoDatabase): the server, the database and a login. Null when the connection is complete. </summary>
    public static string? MissingConnection(CliOptions options)
    {
        if (options.Provider != DatabaseProvider.Sqlite && string.IsNullOrWhiteSpace(options.Server)) return "-S/--server is required.";
        if (string.IsNullOrWhiteSpace(options.Database)) return "-d/--database is required.";
        if (options.Provider != DatabaseProvider.Sqlite && !options.Trusted && string.IsNullOrWhiteSpace(options.UserName)) return "-U/--user is required unless -E/--trusted is used.";
        return null;
    }

    public static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("""
            codegen - CodeGenNew command-line interface (Docs/ARCHITECTURE.md section 7)

            codegen is READ-ONLY against the target database: it introspects schema metadata and writes
            a generated file to the output directory. It never creates, alters, or executes anything in
            the target database, and never modifies files in any other project. What you do with the
            generated output is entirely up to you.

            Whole project (every file of the chosen stacks in one run; see Docs/ARCHITECTURE.md section 6):
              codegen generate -S <server> -d <database> (-E | -U <user> [-P <password>]) --project <name> -o <dir>
                       [--stack api,winui3,react,angular] [--essentials [--groups a,b] [--replace]] [--only SP_Search,CS_Entity] [--table Customer,Item]
                       [--dry-run] [--diff] [--delete-stale] [--force | --write-beside] [--build] [--test]
              Instead of --stack, the technologies can be chosen: --ui winui3|react|angular|blazor|uno, and unless the UI is winui3 also
                       --access-mode routines|ef|ownsql  --api-style rest|odata  --api-language csharp|rust|python
              A combination the rules refuse (Rust with OData, routines on SQLite ...) stops before any file is written. The choices are saved as the project file named by
              --project (default: the database name) unless one of that name exists; an existing project keeps its settings and the choices win for this run.
              A file you edited since the last run, or one no earlier run recorded, is kept and reported (Edited / Unknown).
              --force overwrites it; --write-beside keeps it and writes the new text next to it as <file>.generated.
            Stored values the generated validators would refuse (a database is read, nothing is written):
              codegen check-data -S <server> -d <database> (-E | -U <user> [-P <password>]) --project <name> [--table Customer,Item]
                  Reads the stored rows (read-only, small tables) and lists the values the generated validators would refuse: a phone number with letters, an email without an @, a
                  postal code of the wrong shape, a value past its limit or a date in the future. Fix them, or list the column in PlainTextColumns. Exit code 1 when any is found.
            The files no table drives (App, MainWindow, styles, Program.cs ...), without a database:
              codegen essentials --stack winui3|react|angular|api [--groups app,mainwindow] --project <name> -o <dir> [--replace] [--dry-run]
              codegen essentials --list

            Usage:
              codegen -S <server> -d <database> [-s <schema>] -t <table> -T <template.tt>
                       (-E | -U <user> [-P <password>]) [-o <outputDir>] [--provider SqlServer|PostgreSql|MySql|Sqlite]

            Required (-S and -d, and a login, are not needed for a template whose config says NoDatabase, such as WinUI3_DirectoryListing):
              -S, --server     SQL Server instance name
              -d, --database   Database name
              -t, --table      Table name (not needed for a database-level template such as CS_DbContext or API_Registration, which covers every table of the schema)
              -T, --template   Template file name (e.g. SP_Update.tt), resolved against TemplatesDirectory.
                               Without a version this means the LATEST version; SP_Update_v1.tt pins that exact one.

            Authentication (choose one):
              -E, --trusted    Use Windows Integrated Authentication
              -U, --user       SQL Login username (-P/--password prompted if omitted)
              -P, --password   SQL Login password (omit to be prompted interactively; never persisted)

            Optional:
              -s, --schema     Schema name (default: dbo)
              -o, --output     Output directory (default: Settings.json's OutputDirectory)
              --provider       Database provider: SqlServer (default), PostgreSql or MySql (-S host[:port], -U/-P required; for MySql the schema is the database); Sqlite (-d is the path of the database file; no -S, -U or -P, the schema is main)

            Project settings (namespaces, context name, table lists the templates would otherwise hard-code):
              --project        Name of a Projects\<name>.config file in the CodeGenNew folder. Only ProjectName is
                               required in it; every namespace not listed is derived from it.
              --projects-dir   The folder that holds the project files (default: the Projects folder next to the exe, or Settings.json's ProjectsDirectory).
                               Point the desktop app's Settings.json ProjectsDirectory at the same absolute folder and both tools share one set of projects.
              --set Key=Value  Override any project setting by its name (repeatable), for example --set CurrencyCode=EUR --set "NonNegativeColumns=CreditLimit,Item.Cost".
                               Keys: see Projects\<name>.config / the app's project settings screen.
              --project-name, --view-ns, --viewmodel-ns, --context, --context-ns, --api-ns, --enum-ns,
              --repo-ns, --entity-ns, --naming, --acronyms, --min-year, --max-year
                               Override one setting for this run; wins over the project file.
            """);
    }
}
