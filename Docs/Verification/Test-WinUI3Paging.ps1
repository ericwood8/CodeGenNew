<#
.SYNOPSIS  Drives a generated WinUI3 list page through its paging bar on a scratch database: the page-size box, the last page, and the step back after a delete.
.DESCRIPTION
  1. Makes <Source>_scratch with ScratchDatabase.ps1 (SQL Server, Windows authentication) and adds extra rows to the table, named so that they sort last and have nothing that
     refers to them, so that the last page holds exactly one row at a page size of 10 (51 rows + 10 = 61).
  2. Points the app at the scratch database by editing the appsettings.json next to the exe (the original is put back afterwards).
  3. Opens the table's page from the navigation menu, picks a page size of 10, walks to the last page, checks the label, deletes the single row through the
     confirmation dialog and checks that the grid shows the previous page (no "Page 7 of 6", no empty grid), and that the database lost exactly that row.
  4. Checks that a search that matches nothing says "Nothing found.".
  Always stops the app, restores appsettings.json and drops the scratch database. Exit code 1 when a check failed.
  Build the sample for x64 first:  dotnet build <App>.csproj -p:Platform=x64   (the exe under bin\x64 is the one that is driven).
.EXAMPLE
  .\Test-WinUI3Paging.ps1
  .\Test-WinUI3Paging.ps1 -Exe C:\MySample\My.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\My.exe -Source MyDb -Menu Customer -Table Customer
#>
param(
    [string]$Exe = 'C:\InvoiceSystem\InvoiceSystem.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\InvoiceSystem.exe',
    [string]$Server = 'localhost',
    [string]$Source = 'InvoiceSystem',
    [string]$Menu = 'Customer',        # the navigation item that opens the page
    [string]$Table = 'Customer',
    # added to the scratch copy: 10 customers that sort after every real one and have no invoices, items or summaries
    [string]$SeedSql = @"
DECLARE @i int = 1;
WHILE @i <= 10
BEGIN
    INSERT dbo.Customer (AccountNumber, CustomerName, CustomerStatusId, CreditLimit, IsTaxable, DateAdded, RequireCustomerPO)
    VALUES ('ZZZ' + CAST(@i AS varchar(3)), 'ZZZ TEST ' + RIGHT('0' + CAST(@i AS varchar(3)), 2), (SELECT TOP 1 CustomerStatusId FROM dbo.Customer), 0, 0, GETDATE(), 0);
    SET @i += 1;
END
"@
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'UiAutomation.psm1') -Force -WarningAction SilentlyContinue
$scratch = "${Source}_scratch"
$settings = Join-Path (Split-Path $Exe) 'appsettings.json'
$failures = 0

function Check([bool]$Condition, [string]$Message) {
    if ($Condition) { Write-Host "PASS  $Message" -ForegroundColor Green }
    else { Write-Host "FAIL  $Message" -ForegroundColor Red; $script:failures++ }
}

function Get-PageLabel {
    $el = @(Find-Like 'Page * of *')[0]
    if (-not $el) { $el = @(Find-Like 'Nothing found.')[0] }
    if ($el) { $el.Current.Name } else { '(no label)' }
}

function Get-DeleteButtons { @(Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Delete' }) }

function Get-PageSizeBox { @(Find-Type 'ComboBox')[0] }

function Set-PageSize([int]$Size) {
    $box = Get-PageSizeBox
    Expand-El $box
    Start-Sleep -Milliseconds 800
    $items = $box.FindAll('Descendants', (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
    Select-El ($items | Where-Object { $_.Current.Name -eq "$Size" } | Select-Object -First 1)
    Start-Sleep -Seconds 3
}

function Get-RowCount { (sqlcmd -S $Server -d $scratch -E -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.$Table" | Where-Object { $_ } | Select-Object -First 1).Trim() }

$originalSettings = $null
try {
    & (Join-Path $PSScriptRoot 'ScratchDatabase.ps1') -Provider SqlServer -Source $Source -Server $Server
    sqlcmd -S $Server -d $scratch -E -b -Q $SeedSql
    if ($LASTEXITCODE) { throw 'Adding the extra rows failed.' }
    $rows = [int](Get-RowCount)
    Write-Host "scratch table has $rows rows"
    Check ($rows % 10 -eq 1) 'the scratch table has a number of rows that leaves one row on the last page at a page size of 10'

    $originalSettings = Get-Content $settings -Raw
    if ($originalSettings -notmatch "Database=$Source;") { throw "No 'Database=$Source;' in $settings to point at the scratch copy." }
    Set-Content $settings ($originalSettings -replace "Database=$Source;", "Database=$scratch;") -NoNewline

    Start-App $Exe | Out-Null
    Start-Sleep -Seconds 2
    Select-El (Wait-Name $Menu)
    Start-Sleep -Seconds 4

    $label = Get-PageLabel
    Check ($label -like "Page 1 of * ($rows rows)") "the label shows the page and the total rows ('$label')"
    $box = Get-PageSizeBox
    Check ($null -ne $box) 'the page has a Rows per page box'
    Check (($box.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection() | ForEach-Object { $_.Current.Name }) -eq '20') 'the box starts at 20'

    Set-PageSize 10
    $pages = [math]::Ceiling($rows / 10)
    $label = Get-PageLabel
    Check ($label -eq "Page 1 of $pages ($rows rows)") "page size 10 reads the first page again ('$label')"
    Check ((Get-DeleteButtons).Count -eq 10) 'the first page has 10 rows'

    for ($i = 1; $i -lt $pages; $i++) { Invoke-El (Find-Name 'Next'); Start-Sleep -Milliseconds 1500 }
    $label = Get-PageLabel
    Check ($label -eq "Page $pages of $pages ($rows rows)") "on the last page ('$label')"
    $buttons = Get-DeleteButtons
    Check ($buttons.Count -eq 1) 'the last page holds a single row'

    # delete that row: the confirmation dialog has its own Delete button
    $knownIds = @($buttons | ForEach-Object { ($_.GetRuntimeId()) -join '.' })
    Invoke-El $buttons[0]
    Start-Sleep -Seconds 1
    $confirm = Get-DeleteButtons | Where-Object { $knownIds -notcontains (($_.GetRuntimeId()) -join '.') } | Select-Object -First 1
    Check ($null -ne $confirm) 'the confirmation dialog appears'
    Invoke-El $confirm
    Start-Sleep -Seconds 4

    $expectedPages = [math]::Ceiling(($rows - 1) / 10)
    $label = Get-PageLabel
    Check ($label -eq "Page $expectedPages of $expectedPages ($($rows - 1) rows)") "after the delete the grid steps back to the last page there is ('$label')"
    Check ((Get-DeleteButtons).Count -eq 10) 'the page it stepped back to shows its 10 rows, not an empty grid'
    Check ([int](Get-RowCount) -eq $rows - 1) 'the database lost exactly that one row'

    # a search that matches nothing
    $name = @(Find-Type 'Edit' | Where-Object { $_.Current.Name -like '*Name*' })[0]
    if ($name) {
        Set-Text $name 'zzzz-no-such-customer'
        Send-Keys '{TAB}'
        Invoke-El (Find-Name 'Search')
        Start-Sleep -Seconds 3
        Check ((Get-PageLabel) -eq 'Nothing found.') 'a search that matches nothing says so'
    }
}
finally {
    Stop-App
    if ($null -ne $originalSettings) { Set-Content $settings $originalSettings -NoNewline }
    & (Join-Path $PSScriptRoot 'ScratchDatabase.ps1') -Provider SqlServer -Source $Source -Server $Server -Drop
}
if ($failures -gt 0) { Write-Host "$failures check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host 'All checks passed.' -ForegroundColor Green
