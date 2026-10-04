using Microsoft.Data.Sqlite;

namespace CodeGenNew.Connections;

public static class SqliteConnectionFactory
{
    /// <summary> For SQLite the database is a file: <see cref="ConnectionRequest.DatabaseName"/> is its path, and the server, login and password are not used. The file is opened read-only and without
    /// a connection pool (a pooled connection keeps the file locked after the reader is done), so reading the schema can never change the database. </summary>
    public static string BuildSqliteConnectionString(this ConnectionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DatabaseName))
            throw new ArgumentException("The path of the SQLite database file is required (the database name).", nameof(request));
        if (!File.Exists(request.DatabaseName))
            throw new FileNotFoundException($"The SQLite database file '{request.DatabaseName}' does not exist.", request.DatabaseName);

        return new SqliteConnectionStringBuilder
        {
            DataSource = request.DatabaseName,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ConnectionString;
    }

    public static SqliteConnection CreateSqliteConnection(this ConnectionRequest request) => new(request.BuildSqliteConnectionString());
}
