# WinUI3_SingleRowScreen_v1

The screen of a table the application keeps one row of (a company's settings, a general ledger posting account). The project says which tables with `SingleRowTables=Settings,GeneralLedgerPostingAccount`.

```text
Generates:
    Views/<Table>Page.xaml       the title, the error bar, the fields of the row (tabs when the table has more than one kind of field), Save and Reload
    Views/<Table>Page.xaml.cs    reads the first row (by key) under the app's context gate, makes the form's view model for it, saves, reloads
```

- The fields and the view model are WinUI3_DetailScreen's: the page binds to `<Table>DetailViewModel`, so a change to a field rule reaches the dialog and the page alike. The table still gets its dialog, which nothing opens.
- An empty table shows a blank form and the first Save adds the row. After a Save the page reads the row again, so a second Save updates it. There is no list, no paging, no search, no Clone and no Delete.
- A table in `SingleRowTables` is left out of WinUI3_MasterScreen (no list page); its dialogs are still written, because another table's child grid may open them. Child grids of the table itself are not shown on the page. WinUI3_Screens opens the page for its menu entry.
- Every UI stack shows the table as a form (`SingleRowPlan`); a table served as OData keeps its OData pages, which are lists. Uno has UNO_SingleRowScreen; React, Angular and Blazor make their ordinary page template write the form.
- Needs `ViewModelBase.InTurnAsync` from the WinUI3 essentials (tick Essentials and Overwrite once after updating).
