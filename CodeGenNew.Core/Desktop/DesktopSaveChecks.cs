using CodeGenNew.Core.Allowance;
using CodeGenNew.Core.Party;
using CodeGenNew.Core.Price;
using CodeGenNew.Core.Rate;
using CodeGenNew.Core.Tax;
using CodeGenNew.Core.Tenant;

namespace CodeGenNew.Core.Desktop;

/// <summary> The rules a WinUI3 dialog runs before it saves a row, in the order they run: each topic (subscription limits, gates, content, default rows, parties, snapshots, rates, tax, stored files) writes its own lines
/// or none. Written once for the single-table and the master-detail dialog, which write through their own context and so repeat the checks the API makes. </summary>
public static class DesktopSaveChecks
{
    /// <summary> The check lines for <paramref name="table"/>; <paramref name="partyRows"/> are the editable child tables of a master-detail dialog (the table name and the list of rows being edited) that the party rules also check. </summary>
    public static string Lines(ProjectSettings project, TableModel table, string context, IEnumerable<(string Table, string Rows)>? partyRows = null)
    {
        var text = new System.Text.StringBuilder();
        text.Append(DesktopSubscriptionCode.CheckLines(project, table, context));
        text.Append(DesktopFormCode.GateCheckLines(project, table, context));
        text.Append(DesktopFormCode.ContentCheckLines(project, table, context));
        text.Append(DefaultRowScreens.CheckLines(project, table, context));
        text.Append(PartyScreens.CheckLines(project, table, context));
        foreach (var (child, rows) in partyRows ?? [])
            text.Append(PartyScreens.EditRowsCheckLines(project, table, child, rows, context));
        text.Append(DesktopFormCode.SnapshotCheckLines(project, table, context));
        text.Append(RateScreens.WinUI3DocumentCheck(table, project, context));
        text.Append(PriceScreens.WinUI3Check(table, project, context));
        text.Append(TaxScreens.WinUI3Check(table, project, context));
        text.Append(AllowanceScreens.WinUI3Check(table, project));
        text.Append(AssetDesktopCode.CheckLines(project, table, context));
        return text.ToString();
    }
}
