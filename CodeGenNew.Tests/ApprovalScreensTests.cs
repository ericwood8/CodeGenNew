using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The approvals screens of every front end: the page, the client, the menu entry and the tests that go with them. </summary>
[TestClass]
public class ApprovalScreensTests
{
    [TestMethod]
    public async Task React_gets_the_api_module_the_page_its_tests_and_a_menu_entry()
    {
        var (success, errors, read, files) = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Api,React");

        Assert.IsTrue(success, string.Join(" | ", errors));
        Expect.Contains(read("src/api/approvalsApi.ts"), "toApprove: (segment: string): Promise<ApprovalView[]> => request<ApprovalView[]>(`/api/${segment}/toapprove`)");
        Expect.Contains(read("src/api/approvalsApi.ts"), "{ table: 'TimeSheet', label: 'Time Sheet', segment: 'timesheets' }");
        Expect.Contains(read("src/pages/ApprovalsPage.tsx"), ApprovalWords.WaitingHeading);
        Expect.Contains(read("src/pages/__tests__/ApprovalsPage.test.tsx"), "approvalsApi, 'toApprove'");
        Expect.Contains(read("src/screens.tsx"), "{ path: 'approvals', label: 'Approvals', element: <ApprovalsPage /> }");
    }

    [TestMethod]
    public async Task Angular_gets_the_service_the_component_its_spec_and_a_menu_entry()
    {
        var (success, errors, read, _) = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Api,Angular");

        Assert.IsTrue(success, string.Join(" | ", errors));
        Expect.Contains(read("services/approvals.service.ts"), "return this.http.get<ApprovalView[]>(`api/${segment}/toapprove`);");
        Expect.Contains(read("approvals/approvals.component.html"), ApprovalWords.NoneWaiting);
        Expect.Contains(read("approvals/approvals.component.spec.ts"), "api/timesheets/toapprove");
        Expect.Contains(read("app.routes.ts"), "{ path: 'approvals', label: 'Approvals', component: ApprovalsComponent }");
    }

    [TestMethod]
    public async Task Blazor_gets_the_client_the_page_the_menu_link_and_with_BlazorTests_the_page_tests()
    {
        var (success, errors, read, files) = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Api,Blazor", extra: new() { ["BlazorTests"] = "true" });

        Assert.IsTrue(success, string.Join(" | ", errors));
        Expect.Contains(read("Services/ApprovalsClient.cs"), "public class ApprovalsClient(HttpClient http)");
        Expect.Contains(read("Pages/Approvals.razor"), "@page \"/approvals\"");
        Expect.Contains(read("Layout/NavMenu.razor"), "<NavLink href=\"approvals\">Approvals</NavLink>");
        Expect.Contains(read("Services/ApiClients.cs"), "services.AddScoped<ApprovalsClient>();");
        Expect.Contains(read("ApprovalsPageTests.cs"), "Api.Answer(\"/timesheets/toapprove\"");
    }

    [TestMethod]
    public async Task Uno_gets_the_client_over_the_API_the_page_the_menu_entry_and_with_DesktopTests_the_client_tests()
    {
        var (success, errors, read, _) = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Api,Uno", extra: new() { ["DesktopTests"] = "true" });

        Assert.IsTrue(success, string.Join(" | ", errors));
        Expect.Contains(read("Services/ApprovalsClient.cs"), "public class ApprovalsClient(HttpClient http) : IApprovalsSource");
        Expect.Contains(read("Views/ApprovalsPage.cs"), "public sealed class ApprovalsPage : Page");
        Expect.Contains(read("MainPage.Screens.cs"), "\"Approvals\" => new ApprovalsPage(_api.Approvals, ApprovalsClient.Tables),");
        Expect.Contains(read("Services/ApiClients.cs"), "public ApprovalsClient Approvals => _approvals ??= new(http);");
        Expect.Contains(read("ApprovalsClientTests.cs"), "/api/timesheets/7/request");
    }

    [TestMethod]
    public async Task WinUI3_runs_the_rules_in_process_with_the_same_words_and_a_page_of_its_own_menu_entry()
    {
        var (success, errors, read, files) = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Api,WinUI3", extra: new() { ["DesktopTests"] = "true" });

        Assert.IsTrue(success, string.Join(" | ", errors));
        string service = read("Repositories/ApprovalService.cs");
        Expect.Contains(service, "public sealed class ApprovalService(ShopContext context) : IApprovalsSource");
        Expect.Contains(service, "string? name = DesktopCaller.Name;");
        Expect.Contains(service, ApprovalWords.FourEyes);
        Expect.Contains(service, "public static class DesktopCaller");
        Expect.Contains(read("MainWindow.Screens.cs"), "\"Approvals\" => new ApprovalsPage(new ApprovalService(_context), ApprovalService.Tables),");
        Expect.Contains(read("ApprovalServiceTests.cs"), "DesktopCaller.Name = caller;");
    }

    [TestMethod]
    public async Task With_a_gate_the_gates_class_writes_DesktopCaller_and_the_approval_service_does_not_repeat_it()
    {
        var (success, errors, read, _) = await RowApprovalsTests.GenerateAsync(withSide: true, "TimeSheet.IsPaid; to 1; when Hours>0; approval", stacks: "Api,WinUI3");

        Assert.IsTrue(success, string.Join(" | ", errors));
        Expect.Contains(read("Repositories/Gates.cs"), "public static class DesktopCaller");
        Expect.DoesNotContain(read("Repositories/ApprovalService.cs"), "public static class DesktopCaller");
    }

    [TestMethod]
    public async Task Both_desktop_apps_draw_the_page_from_one_text()
    {
        var uno = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Api,Uno");
        var winui3 = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Api,WinUI3");

        static string Body(string page) => page[page.IndexOf("// The approvals page", StringComparison.Ordinal)..];
        Assert.AreEqual(Body(uno.Read("Views/ApprovalsPage.cs")), Body(winui3.Read("Views/ApprovalsPage.cs")));
    }

    [TestMethod]
    public async Task Without_RowApprovals_no_screen_is_written()
    {
        var (success, _, _, files) = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Api,React,Angular,Blazor,Uno,WinUI3", extra: new() { ["RowApprovals"] = "" });

        Assert.IsTrue(success);
        Assert.IsFalse(files.Any(f => f.Contains("Approvals", StringComparison.Ordinal) || f.Contains("approvals", StringComparison.Ordinal) || f.Contains("ApprovalService") || f.Contains("ApprovalSupport")));
    }
}
