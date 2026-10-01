# InvoiceSystem bug-fix task — progress (delete when done)
Task: fix C:\InvoiceSystem\Bugs.txt UI bugs IN THE TEMPLATES (not hand edits), then regenerate all of C:\InvoiceSystem from templates.
Plan:
- NEW 1: WinUI3_DetailMasterScreen child grid is IsEnabled=False -> make rows clickable (ItemClick) opening the child's dialog; rows carry Entity.
  Child dialog class decided by new project key DetailMasterTables (else <Child>DetailDialog). MasterScreen opens DetailMasterDialog for those tables too.
- OLD 1: grids use SelectionMode=None -> Single.
- OLD 2/3: ContentDialog width: override ContentDialogMaxWidth/MinWidth theme resources in ContentDialog.Resources (LocationDialog in CodeGenNew.App already does it).
- Then regenerate everything for InvoiceSystem using --project InvoiceSystem and compare.
Status: (update below)
Status: templates edited (DetailMaster clickable child grid, widths, MasterScreen selection, DetailMasterTables key). Next: build, run tests for those templates, regenerate InvoiceSystem.
Status: InvoiceSystem regenerated from templates (backup in scratchpad/backup). Remaining: confirm build, report.
Status: DONE. Templates fixed, InvoiceSystem regenerated + builds, full suite 276/276. Awaiting user click-through of C:\InvoiceSystem. Delete this file when committed.
