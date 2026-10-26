namespace CodeGenNew.Core;

/// <summary> The sentences of the row approvals, said once for the API, the desktop service and every approvals screen. </summary>
public static class ApprovalWords
{
    // what the server refuses with
    public const string RequesterUnknown = "You are not a person of the user table, so you cannot request or decide.";
    public const string NotApprover = "Only an administrator or a person with the approver role may decide.";
    public const string NotRequester = "Only the person who made the request can withdraw it.";
    public const string FourEyes = "A request is decided by someone other than the person who made it.";
    public const string AlreadyWaiting = "A request for this row is already waiting for a decision.";
    public const string NothingWaiting = "No request for this row is waiting for a decision.";
    public const string DecidedMeanwhile = "The request was decided by someone else a moment ago.";
    public const string DecidedJustNow = "The request was decided a moment ago.";
    public const string NoSuchRow = "This row no longer exists.";

    // what a screen says
    public const string Title = "Approvals";
    public const string WaitingHeading = "Waiting for your decision";
    public const string NoneWaiting = "Nothing is waiting for your decision.";
    public const string RowHeading = "Request, withdraw or look up one row";
    public const string RowLabel = "Row number";
    public const string ReasonLabel = "Reason";
    public const string NoteLabel = "Note";
    public const string Request = "Request approval";
    public const string Withdraw = "Withdraw";
    public const string Approve = "Approve";
    public const string Reject = "Reject";
    public const string History = "History";
    public const string Refresh = "Refresh";
    public const string NoHistory = "No request has been made for this row.";
    public const string LoadFailed = "Failed to load the approvals.";
    public const string NeedRow = "Enter the number of a row.";
    public const string Requested = "The approval was requested.";
    public const string Withdrawn = "The request was withdrawn.";
    public const string Approved = "The request was approved.";
    public const string Rejected = "The request was rejected.";

    public static string Heading(string tableLabel) => tableLabel + " approvals";
    public static string RowSummary(int row, string status) => $"Row {row}: {status}";
}

/// <summary> One table that has approvals, as a screen lists it. </summary>
public sealed record ApprovalScreenTable(string Table, string Label, string Route, string Segment);

/// <summary> Which tables get an approvals screen and what the screens call them. </summary>
public static class ApprovalScreens
{
    public static IReadOnlyList<ApprovalScreenTable> For(DatabaseModel database, ProjectSettings project) =>
        ApprovalPlan.Resolve(database, project) is { Problems.Count: 0 } plan
            ? plan.Tables.Select(t => new ApprovalScreenTable(t.Table.TableName, Labels.Words(t.Table.TableName), ApiRequests.Route(t.Table), ApiRequests.Route(t.Table)["/api/".Length..])).ToList()
            : [];

    /// <summary> The TypeScript types of the approval wire shape (what the API answers). </summary>
    public static string TypeScript()
    {
        return string.Join("\n", new[]
        {
            "export interface ApprovalView {",
            "  id: number;",
            "  rowId: number;",
            "  revision: number;",
            "  status: string;",
            "  reason: string | null;",
            "  requestedBy: string;",
            "  requestedOn: string;",
            "  decidedBy: string | null;",
            "  decidedOn: string | null;",
            "  decisionNote: string | null;",
            "}"
        }) + "\n";
    }
}
