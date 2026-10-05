using CodeGenNew.Connections;

namespace CodeGenNew.Tests;

[TestClass]
public class ConnectionRequestTests
{
    private static ConnectionRequest Request(DatabaseProvider provider, AuthMode auth, string? user = null) =>
        new() { Provider = provider, ServerName = "server", DatabaseName = "db", AuthMode = auth, UserName = user };

    [TestMethod]
    public void The_description_names_the_provider_and_how_the_login_was_made()
    {
        Assert.AreEqual("SQL Server, Windows Auth", Request(DatabaseProvider.SqlServer, AuthMode.WindowsAuth).DescribeConnection());
        Assert.AreEqual("SQL Server, SQL Login (sa)", Request(DatabaseProvider.SqlServer, AuthMode.SqlLogin, "sa").DescribeConnection());
        Assert.AreEqual("PostgreSQL, SQL Login (ClaudeCode)", Request(DatabaseProvider.PostgreSql, AuthMode.SqlLogin, "ClaudeCode").DescribeConnection());
        Assert.AreEqual("MySQL, SQL Login", Request(DatabaseProvider.MySql, AuthMode.SqlLogin).DescribeConnection());
        Assert.AreEqual("SQLite", Request(DatabaseProvider.Sqlite, AuthMode.SqlLogin).DescribeConnection());
    }
}
