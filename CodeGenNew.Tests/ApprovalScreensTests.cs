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

    private static readonly Dictionary<string, string> PlainForm = new() { ["DetailMasterTables"] = "NoSuchTable" };   // TimeSheet has the side table as a child, so it is a master-detail table unless the project lists others

    [TestMethod]
    [DataRow(false, "TimeSheetDetailMasterPage.tsx")]
    [DataRow(true, "TimeSheetPage.tsx")]
    public async Task React_draws_the_approval_panel_under_the_form_of_a_saved_row_of_an_approval_table(bool plain, string pageFile)
    {
        var (success, errors, read, files) = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Api,React", extra: plain ? PlainForm : null);

        Assert.IsTrue(success, string.Join(" | ", errors));
        string page = read("src/pages/" + pageFile);
        Expect.Contains(page, "import { ApprovalPanel } from './ApprovalPanel';");
        Expect.Contains(page, "{selectedRow.timeSheetId ? <ApprovalPanel segment=\"timesheets\" id={selectedRow.timeSheetId} /> : null}");
        Expect.Contains(read("src/pages/ApprovalPanel.tsx"), "export function ApprovalPanel({ segment, id }");
        Expect.Contains(read("src/pages/ApprovalPanel.tsx"), ApprovalWords.PendingStatus);
        Expect.Contains(read("src/pages/__tests__/ApprovalPanel.test.tsx"), "approvalsApi, 'withdraw'");
        Expect.Contains(read("src/api/approvalsApi.ts"), "export function panelStatus(latest: ApprovalView): string");
        Assert.IsFalse(files.Any(f => f.EndsWith("TimeSheetApprovalPage.tsx", StringComparison.Ordinal)), "the side table has no screen");
    }

    [TestMethod]
    [DataRow(false, "timesheet-detail-master/timesheet-detail-master")]
    [DataRow(true, "timesheet/timesheet")]
    public async Task Angular_draws_the_approval_panel_under_the_form_of_a_saved_row_of_an_approval_table(bool plain, string stem)
    {
        var (success, errors, read, _) = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Api,Angular", extra: plain ? PlainForm : null);

        Assert.IsTrue(success, string.Join(" | ", errors));
        Expect.Contains(read(stem + ".component.html"), "<app-approval-panel *ngIf=\"selectedRow.timeSheetId\" segment=\"timesheets\" [id]=\"selectedRow.timeSheetId\"></app-approval-panel>");
        Expect.Contains(read(stem + ".component.ts"), "import { ApprovalPanelComponent } from '../approvals/approval-panel.component';");
        Expect.Contains(read(stem + ".component.ts"), ", ApprovalPanelComponent");
        Expect.Contains(read("approvals/approval-panel.component.ts"), "export class ApprovalPanelComponent");
        Expect.Contains(read("approvals/approval-panel.component.spec.ts"), "api/timesheets/7/withdraw");
        Expect.Contains(read("services/approvals.service.ts"), "export function panelStatus(latest: ApprovalView): string");
    }

    [TestMethod]
    public async Task Blazor_draws_the_approval_panel_under_the_form_of_a_saved_row_of_an_approval_table()
    {
        var (success, errors, read, _) = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Api,Blazor");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string page = read("Pages/TimeSheetPage.razor");
        Expect.Contains(page, "<ApprovalPanel Segment=\"timesheets\" Id=\"row.TimeSheetId\" />");
        Expect.Contains(page, "@using Shop.Blazor.Components");
        Expect.Contains(read("Components/ApprovalPanel.razor"), "[Parameter, EditorRequired] public int Id { get; set; }");
        Expect.Contains(read("Services/ApprovalsClient.cs"), "public string PanelStatus => $\"Approval status: {Status}, \"");
    }

    [TestMethod]
    [DataRow(false, "TimeSheetDetailMasterDialog")]
    [DataRow(true, "TimeSheetDetailDialog")]
    public async Task Both_desktop_apps_put_the_approval_panel_in_the_dialog_of_a_saved_row_of_an_approval_table(bool plain, string dialog)
    {
        var uno = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Api,Uno", extra: plain ? PlainForm : null);
        var winui3 = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Api,WinUI3", extra: plain ? PlainForm : null);

        Assert.IsTrue(uno.Success && winui3.Success, string.Join(" | ", uno.Errors.Concat(winui3.Errors)));
        Expect.Contains(uno.Read("Views/" + dialog + ".xaml"), "<StackPanel x:Name=\"ApprovalHost\" Spacing=\"8\" />");
        Expect.Contains(uno.Read("Views/" + dialog + ".xaml.cs"), "_approval = new ApprovalPanel(api.Approvals, ApprovalsClient.Tables.First(t => t.Table == \"TimeSheet\"), editing.TimeSheetId);");
        Expect.Contains(uno.Read("Views/" + dialog + ".xaml.cs"), "await _approval.WhenIdle;");
        Expect.Contains(winui3.Read("Views/" + dialog + ".xaml"), "<StackPanel x:Name=\"ApprovalHost\" Spacing=\"8\" />");
        string code = winui3.Read("Views/" + dialog + ".xaml.cs");
        Expect.Contains(code, "new Shop.App.Repositories.ApprovalService(context)");
        Expect.Contains(code, "editing.TimeSheetId, work => ViewModelBase.InTurnAsync(async () => { await work(); return 0; }));");
        Expect.Contains(code, "await _approval.WhenIdle;");
        Expect.Contains(winui3.Read("Views/ApprovalsPage.cs"), "public sealed class ApprovalPanel : StackPanel");
    }

    [TestMethod]
    public async Task Without_RowApprovals_a_form_has_no_approval_panel()
    {
        var react = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Api,React", extra: new() { ["RowApprovals"] = "" });
        var winui3 = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Api,WinUI3", extra: new() { ["RowApprovals"] = "" });

        Expect.DoesNotContain(react.Read("src/pages/TimeSheetDetailMasterPage.tsx"), "ApprovalPanel");
        Expect.DoesNotContain(winui3.Read("Views/TimeSheetDetailMasterDialog.xaml"), "ApprovalHost");
        Expect.DoesNotContain(winui3.Read("Views/TimeSheetDetailMasterDialog.xaml.cs"), "_approval");
    }

    [TestMethod]
    public void The_panel_status_is_the_same_sentence_in_C_and_TypeScript()
    {
        string sentence = ApprovalWords.PanelStatus("Approved", "ann", "bob", "fine");

        Assert.AreEqual("Approval status: Approved, decided by bob: fine", sentence);
        Assert.AreEqual("Approval status: Pending, requested by ann", ApprovalWords.PanelStatus("Pending", "ann", null, null));
        Expect.Contains(ApprovalScreens.PanelStatusTypeScript(), "`Approval status: ${latest.status}, `");
        Expect.Contains(ApprovalScreens.PanelStatusCSharp(), "$\"Approval status: {Status}, \"");
    }

    [TestMethod]
    public async Task Without_RowApprovals_no_screen_is_written()
    {
        var (success, _, _, files) = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Api,React,Angular,Blazor,Uno,WinUI3", extra: new() { ["RowApprovals"] = "" });

        Assert.IsTrue(success);
        Assert.IsFalse(files.Any(f => f.Contains("Approvals", StringComparison.Ordinal) || f.Contains("approvals", StringComparison.Ordinal) || f.Contains("ApprovalService") || f.Contains("ApprovalSupport")));
    }
}
