using Microsoft.Data.SqlClient;

namespace CodeGenNew.SchemaIntrospection;

public enum SpCanDeleteStatus
{
    Verified,
    NotFoundOrWrongSignature
}

/// <summary>
/// Verifies, once per (server, database) rather than on every connection, whether spCanDelete exists
/// with the exact 2-parameter signature CodeGenNew's SP_Delete.tt assumes elsewhere in the developer's
/// own tooling (@deleteFromTable varchar, @deleteId int). This is purely a read-only informational
/// check -- schema metadata only -- and has no effect on what SP_Delete.tt itself generates (see
/// Docs/specs.md section 7.1); the result is cached in SpCanDeleteVerification.config so the live
/// check only ever runs once per database, not on every invocation.
/// </summary>
public static class SpCanDeleteVerifier
{
    private const string ExpectedParam1Name = "@deleteFromTable";
    private const string ExpectedParam1Type = "varchar";
    private const string ExpectedParam2Name = "@deleteId";
    private const string ExpectedParam2Type = "int";

    private const string SignatureQuery = """
        SELECT pm.name AS ParamName, ty.name AS TypeName, pm.is_output AS IsOutput, pm.parameter_id AS ParamId
        FROM sys.procedures p
        INNER JOIN sys.parameters pm ON pm.object_id = p.object_id
        INNER JOIN sys.types ty ON ty.user_type_id = pm.user_type_id
        WHERE p.name = 'spCanDelete'
        ORDER BY pm.parameter_id;
        """;

    /// <summary> Returns the cached result if this (server, database) has already been checked; otherwise
    /// performs the one-time live check and appends the result to the config file. `WasCached` tells the
    /// caller whether a live database check actually happened, so it can decide whether to say anything. </summary>
    public static async Task<(SpCanDeleteStatus Status, bool WasCached)> GetOrVerifyAsync(
        SqlConnection openConnection, string serverName, string databaseName, string configPath,
        CancellationToken cancellationToken = default)
    {
        var cached = LoadEntries(configPath).FirstOrDefault(e =>
            e.ServerName.Equals(serverName, StringComparison.OrdinalIgnoreCase) &&
            e.DatabaseName.Equals(databaseName, StringComparison.OrdinalIgnoreCase));

        if (cached is not null)
            return (cached.Status, true);

        var status = await CheckLiveAsync(openConnection, cancellationToken);
        AppendEntry(configPath, serverName, databaseName, status);
        return (status, false);
    }

    private static async Task<SpCanDeleteStatus> CheckLiveAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var parameters = new List<(string Name, string Type, bool IsOutput)>();

        await using (var command = new SqlCommand(SignatureQuery, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                parameters.Add((
                    reader.GetString(reader.GetOrdinal("ParamName")),
                    reader.GetString(reader.GetOrdinal("TypeName")),
                    reader.GetBoolean(reader.GetOrdinal("IsOutput"))));
            }
        }

        bool matches = parameters.Count == 2
            && parameters[0].Name.Equals(ExpectedParam1Name, StringComparison.OrdinalIgnoreCase)
            && parameters[0].Type.Equals(ExpectedParam1Type, StringComparison.OrdinalIgnoreCase)
            && !parameters[0].IsOutput
            && parameters[1].Name.Equals(ExpectedParam2Name, StringComparison.OrdinalIgnoreCase)
            && parameters[1].Type.Equals(ExpectedParam2Type, StringComparison.OrdinalIgnoreCase)
            && !parameters[1].IsOutput;

        return matches ? SpCanDeleteStatus.Verified : SpCanDeleteStatus.NotFoundOrWrongSignature;
    }

    private sealed record Entry(string ServerName, string DatabaseName, SpCanDeleteStatus Status);

    private static List<Entry> LoadEntries(string configPath)
    {
        var entries = new List<Entry>();
        if (!File.Exists(configPath))
            return entries;

        foreach (string rawLine in File.ReadAllLines(configPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            string[] parts = line.Split('|');
            if (parts.Length < 3)
                continue;

            if (Enum.TryParse<SpCanDeleteStatus>(parts[2].Trim(), out var status))
                entries.Add(new Entry(parts[0].Trim(), parts[1].Trim(), status));
        }

        return entries;
    }

    private static void AppendEntry(string configPath, string serverName, string databaseName, SpCanDeleteStatus status)
    {
        bool isNewFile = !File.Exists(configPath);
        using var writer = new StreamWriter(configPath, append: true);

        if (isNewFile)
        {
            writer.WriteLine("# SpCanDeleteVerification.config");
            writer.WriteLine("# Records, once per (server, database), whether spCanDelete exists with the exact");
            writer.WriteLine("# signature CodeGenNew's SP_Delete.tt assumes (@deleteFromTable varchar, @deleteId int).");
            writer.WriteLine("# This is informational only -- see Docs/specs.md section 7.1 -- and has no effect on");
            writer.WriteLine("# what SP_Delete.tt generates. Delete a line to force CodeGenNew to re-check that database.");
            writer.WriteLine("#");
            writer.WriteLine("# ServerName|DatabaseName|Status|CheckedAtUtc");
        }

        writer.WriteLine($"{serverName}|{databaseName}|{status}|{DateTime.UtcNow:O}");
    }
}
