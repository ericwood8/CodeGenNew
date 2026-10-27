using System.Text;

namespace CodeGenNew.Core;

/// <summary> The bUnit tests of the Blazor page of a log table (LogTables): it lists rows, so they check the heading, a row, that no button adds, changes or deletes one, and that Search sends the first and last day. </summary>
public static class BlazorLogTests
{
    public static string Body(string type, BlazorTestPlan plan, string title, QueueTable? queue = null, ProjectSettings? project = null)
    {
        var row = plan.Row;
        var o = new StringBuilder();
        void L(string s = "") => o.Append(s).Append('\n');
        string page = type + "Page";
        L();
        L("[TestClass]");
        L($"public class {page}Tests : PageTestContext");
        L("{");
        if (row is not null)
        {
            L($"    private const string Row = \"\"\"{row.Json}\"\"\";");
            L();
        }
        if (queue is not null)
        {
            string key = JsonNames.Camel(queue.Key.Name), status = JsonNames.Camel(queue.Status.Name);
            string failedRow = "{ " + string.Join(", ", queue.Table.Columns.Select(c => c == queue.Key ? $"\"{key}\": 7" : c == queue.Status ? $"\"{status}\": \"{QueueStatus.Failed}\"" : $"\"{JsonNames.Camel(c.Name)}\": {ApiSampleValues.Value(c, project!, queue.Table.TableName)}")) + " }";
            L($"    private const string FailedRow = \"\"\"{failedRow}\"\"\";");
            L("    private const string Stats = \"\"\"{ \"pending\": 2, \"claimed\": 1, \"done\": 5, \"failed\": 1, \"oldestPendingId\": 4, \"oldestPendingOn\": \"2026-10-26T08:12:00\" }\"\"\";");
            L();
        }
        L($"    private IRenderedComponent<{page}> Open({(row is not null ? "string? list = null" : "")})");
        L("    {");
        L(row is not null ? "        Api.Answer(\"/search\", list ?? FakeApi.Page(Row, 1));" : "        Api.Answer(\"/search\", FakeApi.Page(\"\", 0));");
        if (queue is not null)
            L("        Api.Answer(\"/stats\", Stats);");
        o.Append(plan.SignIn);
        L($"        return Render<{page}>();");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public void Shows_the_heading()");
        L("    {");
        L("        var page = Open();");
        L($"        StringAssert.Contains(page.Markup, \"<h1>{title.Pluralize()}</h1>\");");
        L("    }");
        if (row is not null)
        {
            L();
            L("    [TestMethod]");
            L("    public void Shows_a_row_the_server_returns()");
            L("    {");
            L("        var page = Open();");
            L($"        page.WaitForAssertion(() => StringAssert.Contains(page.Markup, \"{row.Marker}\"));");
            L("    }");
        }
        L();
        L("    [TestMethod]");
        L("    public void Has_no_button_that_adds_changes_or_deletes_a_row()");
        L("    {");
        L(row is not null ? "        var page = Open();" : "        var page = Open();");
        if (row is not null)
            L($"        page.WaitForAssertion(() => StringAssert.Contains(page.Markup, \"{row.Marker}\"));");
        L("        var labels = page.FindAll(\"button\").Select(b => b.TextContent.Trim()).ToList();");
        L("        CollectionAssert.DoesNotContain(labels, \"Add New\");");
        L("        CollectionAssert.DoesNotContain(labels, \"Edit\");");
        L("        CollectionAssert.DoesNotContain(labels, \"Clone\");");
        L("        CollectionAssert.DoesNotContain(labels, \"Delete\");");
        L("        Assert.IsEmpty(page.FindAll(\"[role=dialog]\"));");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public void Sends_the_first_and_the_last_day_when_it_searches_and_Clear_drops_them()");
        L("    {");
        L("        var page = Open();");
        L("        page.Find(\"input[aria-label='From']\").Input(\"2026-10-01\");");
        L("        page.Find(\"input[aria-label='To']\").Input(\"2026-10-31\");");
        L("        page.FindAll(\"button\").First(b => b.TextContent.Trim() == \"Search\").Click();");
        L($"        page.WaitForAssertion(() => Assert.IsTrue(Api.Requests.Any(r => r.Path.Contains(\"{LogTable.FromParameter}=2026-10-01\") && r.Path.Contains(\"{LogTable.ToParameter}=2026-10-31\"))));");
        L("        Api.Requests.Clear();");
        L("        page.FindAll(\"button\").First(b => b.TextContent.Trim() == \"Clear\").Click();");
        L($"        page.WaitForAssertion(() => Assert.IsTrue(Api.Requests.Any(r => r.Path.Contains(\"/search?\") && !r.Path.Contains(\"{LogTable.FromParameter}=2026\"))));");
        L("    }");
        if (queue is not null)
        {
            L();
            L("    [TestMethod]");
            L("    public void Shows_the_counts_by_status_and_the_oldest_pending_row()");
            L("    {");
            L("        var page = Open();");
            L("        page.WaitForAssertion(() => StringAssert.Contains(page.Markup, \"Pending: 2, claimed: 1, done: 5, failed: 1\"));");
            L("        StringAssert.Contains(page.Markup, \"Oldest pending: #4\");");
            L("    }");
            L();
            L("    [TestMethod]");
            L("    public void Retry_puts_a_failed_row_back_to_pending()");
            L("    {");
            L("        Api.Answer(\"/search\", FakeApi.Page(FailedRow, 1));");
            L("        Api.Answer(\"/stats\", Stats);");
            L("        Api.AnswerWrite(HttpMethod.Post, \"/7/retry\", FailedRow);");
            o.Append(plan.SignIn);
            L($"        var page = Render<{page}>();");
            L("        page.WaitForAssertion(() => Assert.IsTrue(page.FindAll(\"button\").Any(b => b.TextContent.Trim() == \"Retry\")));");
            L("        page.FindAll(\"button\").First(b => b.TextContent.Trim() == \"Retry\").Click();");
            L("        page.WaitForAssertion(() => Assert.IsTrue(Api.Requests.Any(r => r.Method == HttpMethod.Post && r.Path.EndsWith(\"/7/retry\"))));");
            L("    }");
        }
        L("}");
        return o.ToString();
    }
}
