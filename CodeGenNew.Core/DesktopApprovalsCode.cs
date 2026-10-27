using System.Text;

namespace CodeGenNew.Core;

/// <summary> The C# of the approvals screen of the two desktop apps. Both draw one page (<see cref="Page"/>) over <c>IApprovalsSource</c> (<see cref="Support"/>): Uno reads it through the API
/// (<see cref="Client"/>), WinUI3 keeps the rules in process (<see cref="Service"/>), the same rules the API has. The words are <see cref="ApprovalWords"/>. </summary>
public static class DesktopApprovalsCode
{
    /// <summary> The caller of the in-process rules: the name looked up in the user table. It starts as the Windows sign-in. Written by WinUI3_Gates when the project has gates or guards, else by WinUI3_Approvals. </summary>
    public static string CallerClass() =>
        "/// <summary> Who the guards and approvals of the project take the person to be: the name looked up in the user table. It starts as the Windows sign-in; an app with a sign-in of its own sets it after the person signs in. </summary>\n" +
        "public static class DesktopCaller\n{\n    public static string? Name { get; set; } = Environment.UserName;\n}\n\n";

    /// <summary> True when WinUI3_Gates writes <c>DesktopCaller</c> (the project has a gate or a guard). </summary>
    public static bool GatesWriteCaller(ProjectSettings project) => project.ApiAuth && (project.ChangeGates is not null || project.GuardedColumns is not null);

    public static string Support(string servicesNamespace)
    {
        var o = new StringBuilder();
        o.Append($"namespace {servicesNamespace};\n\n");
        o.Append(Fill(SupportText, ("§NONE", ApprovalWords.NoSuchRow), ("§PANELSTATUS", ApprovalScreens.PanelStatusCSharp())));
        return o.ToString();
    }

    /// <summary> The lines of a dialog's or page's constructor that put <c>ApprovalPanel</c> in the element named <c>ApprovalHost</c> when a saved row (<paramref name="row"/> is not null) is shown,
    /// and keep it in <c>_approval</c> so the owner can wait for <c>WhenIdle</c>. <paramref name="turn"/> is the argument that makes the panel's calls take turns with the owner's (empty for none). </summary>
    public static string PanelLines(string indent, string row, string idProperty, string table, string source, string tables, string turn)
    {
        string args = $"{source}, {tables}.First(t => t.Table == \"{table}\"), {row}.{idProperty}" + (turn.Length > 0 ? ", " + turn : "");
        return $"{indent}if ({row} is not null)\n{indent}{{\n{indent}    _approval = new ApprovalPanel({args});\n{indent}    ApprovalHost.Children.Add(_approval);\n{indent}}}\n";
    }

    public static string TablesArray(IReadOnlyList<ApprovalScreenTable> tables) =>
        string.Join(",\n", tables.Select(t => $"        new(\"{t.Table}\", \"{t.Label}\", \"{t.Segment}\")"));

    public static string Client(string servicesNamespace, IReadOnlyList<ApprovalScreenTable> tables) =>
        Fill(ClientText, ("§NS", servicesNamespace), ("§TABLES", TablesArray(tables)));

    public static string Page(string viewNamespace, string servicesNamespace) =>
        Fill(PageText, ("§NS", viewNamespace), ("§SERVICES", servicesNamespace),
            ("§TITLE", ApprovalWords.Title), ("§WAITING", ApprovalWords.WaitingHeading), ("§NONEWAITING", ApprovalWords.NoneWaiting), ("§ROWHEADING", ApprovalWords.RowHeading),
            ("§ROWLABEL", ApprovalWords.RowLabel), ("§REASONLABEL", ApprovalWords.ReasonLabel), ("§NOTELABEL", ApprovalWords.NoteLabel), ("§REQUESTBUTTON", ApprovalWords.Request),
            ("§WITHDRAWBUTTON", ApprovalWords.Withdraw), ("§APPROVEBUTTON", ApprovalWords.Approve), ("§REJECTBUTTON", ApprovalWords.Reject), ("§HISTORYBUTTON", ApprovalWords.History),
            ("§REFRESH", ApprovalWords.Refresh), ("§NOHISTORY", ApprovalWords.NoHistory), ("§LOADFAILED", ApprovalWords.LoadFailed), ("§NEEDROW", ApprovalWords.NeedRow),
            ("§REQUESTED", ApprovalWords.Requested), ("§WITHDRAWN", ApprovalWords.Withdrawn), ("§APPROVED", ApprovalWords.Approved), ("§REJECTED", ApprovalWords.Rejected),
            ("§PANELHEADING", ApprovalWords.PanelHeading), ("§PANELLOADFAILED", ApprovalWords.PanelLoadFailed), ("§PENDINGSTATUS", ApprovalWords.PendingStatus));

    /// <summary> The in-process rules of every approval table: <c>ApprovalService</c> and the hooks. </summary>
    public static string Service(ApprovalPlan plan, IReadOnlyList<ApprovalScreenTable> tables, string servicesNamespace, string contextType, string[] usings, bool includeCaller)
    {
        var user = plan.User!;
        var o = new StringBuilder();
        foreach (string u in usings)
            o.Append($"using {u};\n");
        o.Append("using Microsoft.EntityFrameworkCore;\n\n");
        o.Append($"namespace {servicesNamespace};\n\n");
        if (includeCaller)
            o.Append(CallerClass());
        o.Append(Fill(ServiceHead, ("§CTX", contextType), ("§U", user.Table.TableName), ("§UN", user.UserName.CSharpName()), ("§MAYDECIDE", ApprovalWriter.MayDecideMethod(plan)), ("§KEYTYPE", user.Table.PrimaryKeyColumns[0].CSharpBase()), ("§UK", user.Table.PrimaryKeyColumns[0].CSharpName()), ("§UF", plan.Approvers is null ? $"u.{user.Role!.CSharpName()} == true" : "false"),
            ("§UA", user.Admin is { } admin ? $"u.{admin.CSharpName()} == true" : "false"), ("§W_UNKNOWN", ApprovalWords.RequesterUnknown), ("§W_NOTAPPROVER", ApprovalWords.NotApprover)));
        foreach (var t in plan.Tables)
            o.Append(PerTable(t, contextType));
        o.Append("\n/// <summary> The in-process approval rules (the API's, without the web): the same refusals in the same words. Generated by WinUI3_Approvals. </summary>\npublic sealed class ApprovalService(" + contextType + " context) : IApprovalsSource\n{\n    public static readonly ApprovalTable[] Tables =\n    [\n" + TablesArray(tables) + "\n    ];\n\n");
        string Dispatch(string call) => string.Join("\n", plan.Tables.Select(t => $"            \"{t.Table.TableName}\" => {t.Table.TableName}Approvals.{call},"));
        o.Append("    public Task<List<ApprovalView>> ToApproveAsync(string table) => table switch\n    {\n" + Dispatch("ToApproveAsync(context)") + "\n        _ => throw new ArgumentException(table)\n    };\n\n");
        o.Append("    public Task<List<ApprovalView>> HistoryAsync(string table, int id) => table switch\n    {\n" + Dispatch("HistoryAsync(context, id)") + "\n        _ => throw new ArgumentException(table)\n    };\n\n");
        o.Append("    public Task RequestAsync(string table, int id, string? reason) => table switch\n    {\n" + Dispatch("RequestAsync(context, id, reason)") + "\n        _ => throw new ArgumentException(table)\n    };\n\n");
        o.Append("    public Task DecideAsync(string table, int id, bool approve, string? note) => table switch\n    {\n" + Dispatch("DecideAsync(context, id, approve, note)") + "\n        _ => throw new ArgumentException(table)\n    };\n\n");
        o.Append("    public Task WithdrawAsync(string table, int id) => table switch\n    {\n" + Dispatch("WithdrawAsync(context, id)") + "\n        _ => throw new ArgumentException(table)\n    };\n}\n");
        return o.ToString();
    }

    private static string PerTable(ApprovalTable t, string contextType)
    {
        string Now(ColumnModel c) => c.SqlType == System.Data.SqlDbType.DateTimeOffset ? "DateTimeOffset.Now" : "DateTime.Now";
        return Fill(TableText,
            ("§CTX", contextType), ("§T", t.Table.TableName), ("§A", t.Side.TableName), ("§KT", t.Key.CSharpBase()), ("§K", t.Key.CSharpName()),
            ("§L", t.Link.CSharpName()), ("§ST", t.Status.CSharpName()), ("§RV", t.Revision.CSharpName()), ("§ID", t.SideKey.CSharpName()), ("§RS", t.Reason.CSharpName()),
            ("§RB", t.RequestedBy.CSharpName()), ("§RO", t.RequestedOn.CSharpName()), ("§DB", t.DecidedBy.CSharpName()), ("§DO", t.DecidedOn.CSharpName()), ("§DN", t.DecisionNote.CSharpName()),
            ("§NOWRO", Now(t.RequestedOn)), ("§NOWDO", Now(t.DecidedOn)),
            ("§PENDING", ApprovalStatus.Pending.ToString()), ("§APPROVED", ApprovalStatus.Approved.ToString()), ("§REJECTED", ApprovalStatus.Rejected.ToString()), ("§WITHDRAWN", ApprovalStatus.Withdrawn.ToString()),
            ("§W_UNKNOWN", ApprovalWords.RequesterUnknown), ("§W_NOTAPPROVER", ApprovalWords.NotApprover), ("§W_NOTREQUESTER", ApprovalWords.NotRequester), ("§W_FOUREYES", ApprovalWords.FourEyes),
            ("§W_ALREADY", ApprovalWords.AlreadyWaiting), ("§W_NOTHING", ApprovalWords.NothingWaiting), ("§W_MEANWHILE", ApprovalWords.DecidedMeanwhile), ("§W_JUSTNOW", ApprovalWords.DecidedJustNow),
            ("§W_NOROW", ApprovalWords.NoSuchRow));
    }

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string SupportText = """
/// <summary> One request cycle of a row. </summary>
public class ApprovalView
{
    public int Id { get; set; }
    public int RowId { get; set; }
    public int Revision { get; set; }
    public string Status { get; set; } = "";
    public string? Reason { get; set; }
    public string RequestedBy { get; set; } = "";
    public DateTime RequestedOn { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedOn { get; set; }
    public string? DecisionNote { get; set; }

    /// <summary> The one line a panel shows for this request cycle. </summary>
    public string PanelStatus => §PANELSTATUS;
}

/// <summary> A table that has approvals: the table, the words for it and the route under /api. </summary>
public record ApprovalTable(string Table, string Label, string Segment);

/// <summary> A call the rules refused. The message is the sentence to show. </summary>
public class ApprovalRefused(int status, string message) : Exception(message)
{
    public int Status => status;
}

/// <summary> Where the approvals page gets its rows and sends its requests and decisions: the API (Uno) or the rules in process (WinUI3). A refused call throws <see cref="ApprovalRefused"/>. </summary>
public interface IApprovalsSource
{
    /// <summary> The requests waiting for the signed-in approver (not their own). </summary>
    Task<List<ApprovalView>> ToApproveAsync(string table);
    Task<List<ApprovalView>> HistoryAsync(string table, int id);
    Task RequestAsync(string table, int id, string? reason);
    Task DecideAsync(string table, int id, bool approve, string? note);
    Task WithdrawAsync(string table, int id);
}

""";

    private const string ClientText = """
using System.Net.Http.Json;
using System.Text.Json;

namespace §NS;

/// <summary> The approval routes of the tables of RowApprovals over the API. Generated by UNO_Approvals. </summary>
public class ApprovalsClient(HttpClient http) : IApprovalsSource
{
    public static readonly ApprovalTable[] Tables =
    [
§TABLES
    ];

    private static async Task<HttpResponseMessage> CheckAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return response;
        string body = await response.Content.ReadAsStringAsync();
        string text = $"{(int)response.StatusCode} {response.ReasonPhrase}";
        try
        {
            if (JsonDocument.Parse(body).RootElement.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } sentence)
                text = sentence;
        }
        catch (JsonException)
        {
        }
        throw new ApprovalRefused((int)response.StatusCode, text);
    }

    private static string SegmentOf(string table) => Tables.First(t => t.Table == table).Segment;

    public async Task<List<ApprovalView>> ToApproveAsync(string table) =>
        (await (await CheckAsync(await http.GetAsync($"/api/{SegmentOf(table)}/toapprove"))).Content.ReadFromJsonAsync<List<ApprovalView>>()) ?? [];

    public async Task<List<ApprovalView>> HistoryAsync(string table, int id) =>
        (await (await CheckAsync(await http.GetAsync($"/api/{SegmentOf(table)}/{id}/approvals"))).Content.ReadFromJsonAsync<List<ApprovalView>>()) ?? [];

    public async Task RequestAsync(string table, int id, string? reason) =>
        await CheckAsync(await http.PostAsJsonAsync($"/api/{SegmentOf(table)}/{id}/request", new { reason = string.IsNullOrWhiteSpace(reason) ? null : reason }));

    public async Task DecideAsync(string table, int id, bool approve, string? note) =>
        await CheckAsync(await http.PostAsJsonAsync($"/api/{SegmentOf(table)}/{id}/{(approve ? "approve" : "reject")}", new { note = string.IsNullOrWhiteSpace(note) ? null : note }));

    public async Task WithdrawAsync(string table, int id) =>
        await CheckAsync(await http.PostAsJsonAsync($"/api/{SegmentOf(table)}/{id}/withdraw", new { }));
}
""";

    private const string PageText = """
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using §SERVICES;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace §NS;

// The approvals page: for each table the requests waiting for the signed-in approver with a note and Approve / Reject, and a row's request, withdrawal and history by its number.
public sealed class ApprovalsPage : Page
{
    public ApprovalsPage(IApprovalsSource source, IReadOnlyList<ApprovalTable> tables)
    {
        var root = new StackPanel { Spacing = 24, Padding = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = "§TITLE", Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] });
        foreach (var table in tables)
            root.Children.Add(new ApprovalSection(source, table));
        Content = new ScrollViewer { Content = root };
    }
}

internal sealed class ApprovalSection : StackPanel
{
    private readonly IApprovalsSource _source;
    private readonly ApprovalTable _table;
    private readonly StackPanel _waiting = new() { Spacing = 6 };
    private readonly TextBlock _waitingMessage = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _row = new() { Width = 120 };
    private readonly TextBox _reason = new() { Width = 320 };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _history = new() { Spacing = 4 };

    public ApprovalSection(IApprovalsSource source, ApprovalTable table)
    {
        _source = source;
        _table = table;
        Spacing = 8;
        AutomationProperties.SetName(this, table.Label + " approvals");
        AutomationProperties.SetName(_row, "§ROWLABEL");
        AutomationProperties.SetName(_reason, "§REASONLABEL");

        Children.Add(new TextBlock { Text = table.Label + " approvals", Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        var refresh = new Button { Content = "§REFRESH" };
        refresh.Click += async (_, _) => await LoadAsync();
        Children.Add(new TextBlock { Text = "§WAITING", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        Children.Add(refresh);
        Children.Add(_waitingMessage);
        Children.Add(_waiting);
        Children.Add(new TextBlock { Text = "§ROWHEADING", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });

        var entry = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        entry.Children.Add(new TextBlock { Text = "§ROWLABEL", VerticalAlignment = VerticalAlignment.Center });
        entry.Children.Add(_row);
        entry.Children.Add(new TextBlock { Text = "§REASONLABEL", VerticalAlignment = VerticalAlignment.Center });
        entry.Children.Add(_reason);
        Children.Add(entry);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(Command("§REQUESTBUTTON", async id => { await _source.RequestAsync(_table.Table, id, _reason.Text); return "§REQUESTED"; }));
        buttons.Children.Add(Command("§WITHDRAWBUTTON", async id => { await _source.WithdrawAsync(_table.Table, id); return "§WITHDRAWN"; }));
        var lookUp = new Button { Content = "§HISTORYBUTTON" };
        lookUp.Click += async (_, _) =>
        {
            if (!RowNumber(out int id))
                return;
            try
            {
                _message.Text = "";
                await ShowHistoryAsync(id);
            }
            catch (ApprovalRefused ex)
            {
                _message.Text = ex.Message;
            }
        };
        buttons.Children.Add(lookUp);
        Children.Add(buttons);
        Children.Add(_message);
        Children.Add(_history);

        Loaded += async (_, _) => await LoadAsync();
    }

    private Button Command(string content, Func<int, Task<string>> run)
    {
        var button = new Button { Content = content };
        button.Click += async (_, _) =>
        {
            if (RowNumber(out int id))
                await ActAsync(id, () => run(id));
        };
        return button;
    }

    private bool RowNumber(out int id)
    {
        if (int.TryParse(_row.Text.Trim(), out id))
            return true;
        _message.Text = "§NEEDROW";
        return false;
    }

    private async Task ActAsync(int id, Func<Task<string>> run)
    {
        try
        {
            _message.Text = await run();
            await LoadAsync();
            await ShowHistoryAsync(id);
        }
        catch (ApprovalRefused ex)
        {
            _message.Text = ex.Message;
        }
    }

    private async Task LoadAsync()
    {
        _waiting.Children.Clear();
        _waitingMessage.Text = "";
        try
        {
            var rows = await _source.ToApproveAsync(_table.Table);
            if (rows.Count == 0)
                _waitingMessage.Text = "§NONEWAITING";
            foreach (var w in rows)
                _waiting.Children.Add(WaitingRow(w));
        }
        catch (ApprovalRefused ex)
        {
            // a person who may not decide is told so in the rules' words; anything else is a failed load
            _waitingMessage.Text = ex.Status == 403 ? ex.Message : "§LOADFAILED";
        }
        catch (System.Net.Http.HttpRequestException)
        {
            _waitingMessage.Text = "§LOADFAILED";
        }
    }

    private UIElement WaitingRow(ApprovalView w)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = $"Row {w.RowId}, requested by {w.RequestedBy} on {w.RequestedOn:g}" + (string.IsNullOrEmpty(w.Reason) ? "" : ": " + w.Reason), VerticalAlignment = VerticalAlignment.Center });
        var note = new TextBox { Width = 220 };
        AutomationProperties.SetName(note, $"§NOTELABEL for row {w.RowId}");
        panel.Children.Add(note);
        foreach (var (text, approve, done) in new[] { ("§APPROVEBUTTON", true, "§APPROVED"), ("§REJECTBUTTON", false, "§REJECTED") })
        {
            var button = new Button { Content = text };
            AutomationProperties.SetName(button, $"{text} row {w.RowId}");
            button.Click += async (_, _) => await ActAsync(w.RowId, async () => { await _source.DecideAsync(_table.Table, w.RowId, approve, note.Text); return done; });
            panel.Children.Add(button);
        }
        return panel;
    }

    private async Task ShowHistoryAsync(int id)
    {
        var cycles = await _source.HistoryAsync(_table.Table, id);
        _history.Children.Clear();
        if (cycles.Count == 0)
        {
            _history.Children.Add(new TextBlock { Text = "§NOHISTORY" });
            return;
        }
        foreach (var h in cycles)
            _history.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = $"Revision {h.Revision}: {h.Status}, requested by {h.RequestedBy} on {h.RequestedOn:g}" + (string.IsNullOrEmpty(h.Reason) ? "" : $" ({h.Reason})")
                    + (h.DecidedBy is null ? "" : $", decided by {h.DecidedBy} on {h.DecidedOn:g}") + (string.IsNullOrEmpty(h.DecisionNote) ? "" : $": {h.DecisionNote}")
            });
    }
}

/// <summary> The approval of one saved row, on its edit form: the latest request, with Request while none waits and Withdraw while one does. The rules say who may do what. <c>turn</c> lets the
/// caller make the panel's calls take turns with its own (the WinUI3 apps share one context between the dialog and the panel); <see cref="WhenIdle"/> is the last call, for a dialog that waits before it lets go of the context. </summary>
public sealed class ApprovalPanel : StackPanel
{
    private readonly IApprovalsSource _source;
    private readonly ApprovalTable _table;
    private readonly int _id;
    private readonly Func<Func<Task>, Task> _turn;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _requestRow = new() { Orientation = Orientation.Horizontal, Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly TextBox _reason = new() { Width = 280 };
    private readonly Button _withdraw = new() { Content = "§WITHDRAWBUTTON", Visibility = Visibility.Collapsed };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };

    public Task WhenIdle { get; private set; } = Task.CompletedTask;

    public ApprovalPanel(IApprovalsSource source, ApprovalTable table, int id, Func<Func<Task>, Task>? turn = null)
    {
        _source = source;
        _table = table;
        _id = id;
        _turn = turn ?? (work => work());
        Spacing = 8;
        AutomationProperties.SetName(this, "§PANELHEADING");
        AutomationProperties.SetName(_reason, "§REASONLABEL");

        var request = new Button { Content = "§REQUESTBUTTON" };
        request.Click += async (_, _) => await Track(ActAsync(() => _source.RequestAsync(_table.Table, _id, _reason.Text), "§REQUESTED"));
        _withdraw.Click += async (_, _) => await Track(ActAsync(() => _source.WithdrawAsync(_table.Table, _id), "§WITHDRAWN"));
        _requestRow.Children.Add(new TextBlock { Text = "§REASONLABEL", VerticalAlignment = VerticalAlignment.Center });
        _requestRow.Children.Add(_reason);
        _requestRow.Children.Add(request);

        Children.Add(new TextBlock { Text = "§PANELHEADING", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        Children.Add(_status);
        Children.Add(_requestRow);
        Children.Add(_withdraw);
        Children.Add(_message);
        Loaded += async (_, _) => await Track(RefreshAsync());
    }

    private Task Track(Task call) => WhenIdle = call;

    private async Task RefreshAsync()
    {
        try
        {
            List<ApprovalView> cycles = [];
            await _turn(async () => cycles = await _source.HistoryAsync(_table.Table, _id));
            var latest = cycles.OrderByDescending(c => c.Revision).FirstOrDefault();
            _status.Text = latest is null ? "§NOHISTORY" : latest.PanelStatus;
            bool pending = latest?.Status == "§PENDINGSTATUS";
            _requestRow.Visibility = pending ? Visibility.Collapsed : Visibility.Visible;
            _withdraw.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is ApprovalRefused or System.Net.Http.HttpRequestException)
        {
            _message.Text = "§PANELLOADFAILED";
        }
    }

    private async Task ActAsync(Func<Task> call, string done)
    {
        try
        {
            await _turn(call);
            _message.Text = done;
            _reason.Text = "";
            await RefreshAsync();
        }
        catch (ApprovalRefused ex)
        {
            _message.Text = ex.Message;
        }
    }
}
""";

    private const string ServiceHead = """
/// <summary> What happened, handed to the project's hooks. <c>Context</c> is the same context the rule saves with, so a change made in <c>OnDecided</c> is saved in the decision's transaction. </summary>
public sealed record ApprovalEvent(§CTX Context, string Table, object RowId, string Action, string Person, int Revision);

/// <summary> The two places a project adds its own rules. <c>Validate</c> runs before a request, an approval, a rejection or a withdrawal and must have no side effects: a sentence refuses it.
/// <c>OnDecided</c> runs after an approval or rejection is written and before it is committed. </summary>
public static class ApprovalHooks
{
    public static Func<ApprovalEvent, Task<string?>>? Validate { get; set; }
    public static Func<ApprovalEvent, Task>? OnDecided { get; set; }
}

/// <summary> The person at the keyboard as the user table knows them: <c>MayDecide</c> is an administrator or a person whose role flag is set; <c>Key</c> is their key in the user table. </summary>
public sealed record ApprovalPerson(string Name, bool MayDecide, §KEYTYPE Key);

public static class ApprovalPeople
{
    public static async Task<ApprovalPerson> RequireAsync(§CTX context)
    {
        string? name = DesktopCaller.Name;
        var row = string.IsNullOrEmpty(name) ? null : await context.Set<§U>().AsNoTracking().Where(u => u.§UN == name).Select(u => new { Name = u.§UN, Key = u.§UK, Flag = §UF, Admin = §UA }).FirstOrDefaultAsync();
        return row is null ? throw new ApprovalRefused(403, "§W_UNKNOWN") : new ApprovalPerson(row.Name!, row.Flag || row.Admin, row.Key);
    }

§MAYDECIDE

    public static async Task<ApprovalPerson> RequireApproverAsync(§CTX context, string table)
    {
        var person = await RequireAsync(context);
        return await MayDecideAsync(context, person, table) ? person : throw new ApprovalRefused(403, "§W_NOTAPPROVER");
    }
}

public static class ApprovalNames
{
    public static string Of(int status) => status switch { 1 => "Pending", 2 => "Approved", 3 => "Rejected", 4 => "Withdrawn", _ => "Unknown" };
}

""";

    private const string TableText = """
/// <summary> The approval rules of §T over §A, one request cycle per row. Generated by WinUI3_Approvals. </summary>
public static class §TApprovals
{
    private static ApprovalView View(§A a) => new() { Id = a.§ID, RowId = a.§L, Revision = a.§RV, Status = ApprovalNames.Of(a.§ST), Reason = a.§RS, RequestedBy = a.§RB, RequestedOn = a.§RO, DecidedBy = a.§DB, DecidedOn = a.§DO, DecisionNote = a.§DN };

    private static Task<§A?> LatestAsync(§CTX context, §KT id) =>
        context.Set<§A>().AsNoTracking().Where(a => a.§L == id).OrderByDescending(a => a.§RV).FirstOrDefaultAsync();

    private static async Task ValidateAsync(§CTX context, §KT id, string action, string person, int revision)
    {
        if (ApprovalHooks.Validate is { } validate && await validate(new ApprovalEvent(context, "§T", id, action, person, revision)) is { } refusal)
            throw new ApprovalRefused(409, refusal);
    }

    public static async Task RequestAsync(§CTX context, §KT id, string? reason)
    {
        var person = await ApprovalPeople.RequireAsync(context);
        if (!await context.Set<§T>().AsNoTracking().AnyAsync(r => r.§K == id))
            throw new ApprovalRefused(404, "§W_NOROW");
        var latest = await LatestAsync(context, id);
        if (latest is not null && latest.§ST == §PENDING)
            throw new ApprovalRefused(409, "§W_ALREADY");
        int revision = (latest?.§RV ?? 0) + 1;
        await ValidateAsync(context, id, "request", person.Name, revision);
        context.Add(new §A { §L = id, §ST = §PENDING, §RS = string.IsNullOrWhiteSpace(reason) ? null : reason, §RB = person.Name, §RO = §NOWRO, §RV = revision });
        await context.SaveChangesAsync();
    }

    public static async Task DecideAsync(§CTX context, §KT id, bool approve, string? note)
    {
        var person = await ApprovalPeople.RequireApproverAsync(context, "§T");
        var latest = await LatestAsync(context, id);
        if (latest is null || latest.§ST != §PENDING)
            throw new ApprovalRefused(409, "§W_NOTHING");
        if (string.Equals(latest.§RB, person.Name, StringComparison.OrdinalIgnoreCase))
            throw new ApprovalRefused(403, "§W_FOUREYES");
        await ValidateAsync(context, id, approve ? "approve" : "reject", person.Name, latest.§RV);
        await using var transaction = await context.Database.BeginTransactionAsync();
        int status = approve ? §APPROVED : §REJECTED;
        string? text = string.IsNullOrWhiteSpace(note) ? null : note;
        var decidedOn = §NOWDO;
        int changed = await context.Set<§A>().Where(a => a.§ID == latest.§ID && a.§ST == §PENDING)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.§ST, status).SetProperty(a => a.§DB, person.Name).SetProperty(a => a.§DO, decidedOn).SetProperty(a => a.§DN, text));
        if (changed == 0)
            throw new ApprovalRefused(409, "§W_MEANWHILE");
        if (ApprovalHooks.OnDecided is { } decided)
            await decided(new ApprovalEvent(context, "§T", id, approve ? "approve" : "reject", person.Name, latest.§RV));
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public static async Task WithdrawAsync(§CTX context, §KT id)
    {
        var person = await ApprovalPeople.RequireAsync(context);
        var latest = await LatestAsync(context, id);
        if (latest is null || latest.§ST != §PENDING)
            throw new ApprovalRefused(409, "§W_NOTHING");
        if (!string.Equals(latest.§RB, person.Name, StringComparison.OrdinalIgnoreCase))
            throw new ApprovalRefused(403, "§W_NOTREQUESTER");
        await ValidateAsync(context, id, "withdraw", person.Name, latest.§RV);
        var decidedOn = §NOWDO;
        int changed = await context.Set<§A>().Where(a => a.§ID == latest.§ID && a.§ST == §PENDING)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.§ST, §WITHDRAWN).SetProperty(a => a.§DB, person.Name).SetProperty(a => a.§DO, decidedOn));
        if (changed == 0)
            throw new ApprovalRefused(409, "§W_JUSTNOW");
    }

    public static async Task<List<ApprovalView>> ToApproveAsync(§CTX context)
    {
        var person = await ApprovalPeople.RequireApproverAsync(context, "§T");
        var waiting = await context.Set<§A>().AsNoTracking().Where(a => a.§ST == §PENDING && a.§RB != person.Name).OrderBy(a => a.§RO).ToListAsync();
        return waiting.Select(View).ToList();
    }

    public static async Task<List<ApprovalView>> HistoryAsync(§CTX context, §KT id)
    {
        var cycles = await context.Set<§A>().AsNoTracking().Where(a => a.§L == id).OrderByDescending(a => a.§RV).ToListAsync();
        return cycles.Select(View).ToList();
    }
}

""";
}
