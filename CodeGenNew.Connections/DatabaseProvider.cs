namespace CodeGenNew.Connections;

public enum DatabaseProvider
{
    SqlServer,
    MySql,
    PostgreSql,
    Sqlite
}

public enum AuthMode
{
    SqlLogin,
    WindowsAuth
}
