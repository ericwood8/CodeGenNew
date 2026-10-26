# UNO_SingleRowScreen_v1

The Uno Platform twin of WinUI3_SingleRowScreen: the screen of a table the application keeps one row of (the `SingleRowTables` setting).

```text
Generates:
    Views/<Table>Page.xaml       the title, the error bars, the fields of the row (tabs when the table has more than one kind of field), Save and Reload
    Views/<Table>Page.xaml.cs    reads the first row through the table's API client (GetPageAsync(1, 1)), makes the form's view model for it, saves, reloads
```

- The fields and the view model are UNO_DetailScreen's, guarded columns included, so a change to a field rule reaches the dialog and the page alike. The table still gets its dialogs, which nothing opens.
- A read that fails shows its sentence in an error bar of the page; a refused save shows the API's own words in the form's bar.
- With Auth=true Save shows only to the roles WriteRoles names for the table.
- An empty table shows a blank form and the first Save adds the row; after a Save the page reads the row again. There is no list, no paging, no Clone and no Delete.
