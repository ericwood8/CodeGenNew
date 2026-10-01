using CodeGenNew.Connections;

namespace CodeGenNew.Cli;

public class ArgumentParseException(string message) : Exception(message);

public static class ArgumentParser
{
    public static CliOptions Parse(string[] args)
    {
        string? server = null, database = null, schema = null, table = null, template = null;
        string? outputDirectory = null, userName = null, password = null, project = null;
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool trusted = false;
        var provider = DatabaseProvider.SqlServer;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

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
                case "--project-name": overrides["ProjectName"] = Value(); break;
                case "--view-ns": overrides["ViewNamespace"] = Value(); break;
                case "--viewmodel-ns": overrides["ViewModelNamespace"] = Value(); break;
                case "--context": overrides["ContextName"] = Value(); break;
                case "--context-ns": overrides["ContextNamespace"] = Value(); break;
                case "--api-ns": overrides["ApiNamespace"] = Value(); break;
                case "--enum-ns": overrides["EnumNamespace"] = Value(); break;
                case "--repo-ns": overrides["RepoNamespace"] = Value(); break;
                case "--entity-ns": overrides["EntityNamespace"] = Value(); break;
                case "--min-year": overrides["MinYear"] = Value(); break;
                case "--max-year": overrides["MaxYear"] = Value(); break;
                case "--provider":
                    string providerText = Value();
                    if (!Enum.TryParse(providerText, ignoreCase: true, out provider))
                        throw new ArgumentParseException($"Unknown --provider '{providerText}'. Valid values: SqlServer, MySql.");
                    break;
                default:
                    throw new ArgumentParseException($"Unrecognized argument: '{arg}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(server)) throw new ArgumentParseException("-S/--server is required.");
        if (string.IsNullOrWhiteSpace(database)) throw new ArgumentParseException("-d/--database is required.");
        if (string.IsNullOrWhiteSpace(table)) throw new ArgumentParseException("-t/--table is required.");
        if (string.IsNullOrWhiteSpace(template)) throw new ArgumentParseException("-T/--template is required.");
        if (!trusted && string.IsNullOrWhiteSpace(userName))
            throw new ArgumentParseException("-U/--user is required unless -E/--trusted is used.");

        foreach (string yearKey in new[] { "MinYear", "MaxYear" })
        {
            if (overrides.TryGetValue(yearKey, out string? yearText) && !int.TryParse(yearText, out _))
                throw new ArgumentParseException($"--{(yearKey == "MinYear" ? "min" : "max")}-year must be a whole number, not '{yearText}'.");
        }

        var options = new CliOptions
        {
            Provider = provider,
            Server = server,
            Database = database,
            Schema = schema ?? "dbo",
            Table = table,
            Template = template,
            OutputDirectory = outputDirectory,
            Trusted = trusted,
            UserName = userName,
            Password = password,
            Project = project
        };
        foreach (var (key, value) in overrides)
            options.ProjectOverrides[key] = value;
        return options;
    }

    public static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("""
            codegen - CodeGenNew command-line interface (Docs/specs.md section 10)

            codegen is READ-ONLY against the target database: it introspects schema metadata and writes
            a generated file to the output directory. It never creates, alters, or executes anything in
            the target database, and never modifies files in any other project. What you do with the
            generated output is entirely up to you.

            Usage:
              codegen -S <server> -d <database> [-s <schema>] -t <table> -T <template.tt>
                       (-E | -U <user> [-P <password>]) [-o <outputDir>] [--provider SqlServer|MySql]

            Required:
              -S, --server     SQL Server instance name
              -d, --database   Database name
              -t, --table      Table name
              -T, --template   Template file name (e.g. SP_Update.tt), resolved against TemplatesDirectory.
                               Without a version this means the LATEST version; SP_Update_v1.tt pins that exact one.

            Authentication (choose one):
              -E, --trusted    Use Windows Integrated Authentication
              -U, --user       SQL Login username (-P/--password prompted if omitted)
              -P, --password   SQL Login password (omit to be prompted interactively; never persisted)

            Optional:
              -s, --schema     Schema name (default: dbo)
              -o, --output     Output directory (default: Settings.json's OutputDirectory)
              --provider       Database provider: SqlServer (default) or MySql (not implemented yet)

            Project settings (namespaces, context name, table lists the templates would otherwise hard-code):
              --project        Name of a Projects\<name>.config file in the CodeGenNew folder. Only ProjectName is
                               required in it; every namespace not listed is derived from it.
              --project-name, --view-ns, --viewmodel-ns, --context, --context-ns, --api-ns, --enum-ns,
              --repo-ns, --entity-ns, --min-year, --max-year
                               Override one setting for this run; wins over the project file.
            """);
    }
}
