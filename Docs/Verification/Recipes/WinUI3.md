# Verifying a generated WinUI3 app

1. Build: `dotnet build <App>.csproj -p:Platform=x64`. A running copy locks the files (`MSB3026`): close it first.
2. The app reads its connection from its own settings file; point it at the scratch copy, with the password in the environment (`PGPASSWORD`, `MYSQL_PWD`), not in the file.
3. Drive it with `UiAutomation.psm1`: `Start-App`, `Find-Name 'Customer'`, `Select-El` a navigation item, `Invoke-El` a button, `List-Names` to see what a screen offers. `Test-AppDialogs.ps1` is a worked example against the CodeGenNew app itself.
4. UI Automation traps: a TextBox commits its text on focus loss (send `{TAB}` after `Set-Text`); a ContentDialog stays open until its button is invoked, and only one can be open at a time; a deleted row asks for a confirmation.
5. Always `Stop-App` (the scripts do it in `finally`).
6. `Test-WinUI3Paging.ps1` is the worked example for a generated list page against a scratch database (page size, last page, delete and step back). Build the x64 output first (`-p:Platform=x64`): the sample's `Regenerate.sh` builds the default platform, so the exe under `bin\x64` can be stale. The page-size box is the first `ComboBox` on the page (`Find-Name 'Rows per page'` finds the label beside it).
