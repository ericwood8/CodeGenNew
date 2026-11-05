param([string]$Exe, [string]$Db)
# Clicks through a generated WinUI3 app over a scratch SQLite copy of Schemas/features.sql (seed the sign-ins first with the hash of features_check.py) for the refresh and the child grid.
# Settings as winui3_features_check.ps1 plus FeatureFields=Project.StartedOn:csv-export,EmployeeTenant.Role:csv-export and FeatureRefreshSeconds=5.
# Flags are changed in the database while the app runs: the menu follows within the interval without a click, and the child grid of the Employee dialog loses its Role column at the next open.
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'

function Run($sql) { python -c "import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute(sys.argv[2]); c.commit()" $Db $sql }
function Names { (List-Names | Select-Object -First 160) -join ' | ' }
function Press($caption) { Invoke-El (Find-Type 'Button' | Where-Object { $_.Current.Name -eq $caption } | Select-Object -First 1); Start-Sleep -Milliseconds 1500 }
function Count($text) { @(List-Names | Where-Object { $_ -like "*$text*" }).Count }

Run 'delete from FeatureOverride'
Start-App -Path $Exe | Out-Null
try {
    Wait-Name 'Sign in' | Out-Null
    Set-Text (Find-Name 'User name') 'ada'
    Set-Text (Find-Name 'Password') 'secret-pass'
    Invoke-El (Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Sign in' } | Select-Object -First 1)
    Start-Sleep -Seconds 4

    Assert-That ((Names) -notlike '*ListItem: Project*') 'Project is not in the menu while new-invoice-screen is off'

    Select-El (Find-Name 'Employee')
    Start-Sleep -Seconds 2
    Press 'Edit'
    Start-Sleep -Seconds 2
    $roleOn = Count 'Role'
    Write-Host ('Role names with csv-export on: ' + $roleOn)
    Assert-That ($roleOn -ge 1) 'the child grid shows Role while csv-export is on'
    Press 'Cancel'

    Run 'insert into FeatureOverride (FeatureId, TenantId, IsEnabled) values (1, 1, 1)'
    Run 'insert into FeatureOverride (FeatureId, TenantId, IsEnabled) values (2, 1, 0)'
    Start-Sleep -Seconds 9
    Assert-That ((Names) -like '*ListItem: Project*') 'Project joins the menu by itself after the flag is switched on in the database'

    Select-El (Find-Name 'Employee')
    Start-Sleep -Seconds 2
    Press 'Edit'
    Start-Sleep -Seconds 2
    $roleOff = Count 'Role'
    Write-Host ('Role names with csv-export off: ' + $roleOff)
    Assert-That ($roleOff -lt $roleOn) 'the child grid leaves out Role while csv-export is off'
    Press 'Cancel'
} finally { Stop-App }
Run 'delete from FeatureOverride'
if ($script:Failed) { exit 1 }
