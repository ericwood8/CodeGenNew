namespace CodeGenNew.Core;

/// <summary> One tab of a generated edit form: its title and the columns whose fields it holds. </summary>
public sealed record FormPage(string Header, List<ColumnModel> Columns);

/// <summary> How a generated edit form splits its fields into tabs, the same on every platform (WinUI3, Angular, React). Every ordinary field goes on a "Main" tab
/// (the one shown first), Billing* and Shipping* fields on a "Billing &amp; Shipping" tab, and long-text fields (ColumnModel.IsLongTextColumn: Note, Notes, Comment,
/// Comments, Remarks, Memo, or long text -- the columns a grid never shows) on a "Notes" tab. A tab with no fields is left out. A form with nothing to split off keeps
/// one untitled page, so a narrow table is still a single column. </summary>
public static class FormPages
{
    public const string MainHeader = "Main";
    public const string BillingShippingHeader = "Billing & Shipping";
    public const string NotesHeader = "Notes";

    public static bool IsBillingOrShipping(ColumnModel c) =>
        c.Name.StartsWithIgnoreCase("Billing") || c.Name.StartsWithIgnoreCase("Shipping");

    /// <param name="columns"> The columns the form shows, in the order they should appear. </param>
    public static List<FormPage> For(IReadOnlyList<ColumnModel> columns)
    {
        var notes = columns.Where(c => c.IsLongTextColumn).ToList();
        var billingShipping = columns.Where(c => !c.IsLongTextColumn && IsBillingOrShipping(c)).ToList();
        var main = columns.Where(c => !c.IsLongTextColumn && !IsBillingOrShipping(c)).ToList();
        if (main.Count == 0 || (billingShipping.Count == 0 && notes.Count == 0))
            return [new FormPage("", columns.ToList())];
        return new[] { new FormPage(MainHeader, main), new FormPage(BillingShippingHeader, billingShipping), new FormPage(NotesHeader, notes) }
            .Where(p => p.Columns.Count > 0).ToList();
    }
}
