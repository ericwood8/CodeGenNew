param([string]$Exe, [string]$Db)
# Clicks through a generated WinUI3 app over a scratch SQLite copy of Schemas/features_noplan.sql (seed the sign-ins first with a copy of features_check.py's hash).
# Settings: Auth, ApiAuth, TenantTable, TenantMembership, UserTable, UserOwnerColumn=EmployeeId, FeatureTables=Feature,FeatureOverride,
# FeatureScreens=Project:new-invoice-screen, FeatureFields=Project.StartedOn:csv-export. Build the app with: dotnet build -p:Platform=x64
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'

function Rows($sql) { (python -c "import sqlite3,sys; print(sqlite3.connect(sys.argv[1]).execute(sys.argv[2]).fetchall())" $Db $sql) }
function Names { (List-Names | Select-Object -First 120) -join ' | ' }
function Press($caption) { Invoke-El (Find-Type 'Button' | Where-Object { $_.Current.Name -eq $caption } | Select-Object -First 1); Start-Sleep -Milliseconds 1500 }
function Count($text) { @(List-Names | Where-Object { $_ -like "*$text*" }).Count }
function Box($flag) { Find-Type 'CheckBox' | Where-Object { $_.Current.Name -eq $flag } | Select-Object -First 1 }

python -c "import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute('delete from FeatureOverride'); c.commit()" $Db
Start-App -Path $Exe | Out-Null
try {
    Wait-Name 'Sign in' | Out-Null
    Set-Text (Find-Name 'User name') 'ada'
    Set-Text (Find-Name 'Password') 'secret-pass'
    Invoke-El (Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Sign in' } | Select-Object -First 1)
    Start-Sleep -Seconds 4

    $menu = Names
    Write-Host $menu
    Assert-That ($menu -notlike '*ListItem: Project*') 'Project is not in the menu while new-invoice-screen is off'
    Assert-That ($menu -like '*Features*') 'the administrator has the Features entry'

    Select-El (Find-Name 'Features')
    Start-Sleep -Seconds 2
    $page = Names
    Assert-That ($page -like '*csv-export*' -and $page -like '*new-invoice-screen*') 'the page lists the flags'
    Assert-That ((Box 'csv-export').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq 'On') 'csv-export is on by default'

    Toggle-El (Box 'new-invoice-screen')
    Start-Sleep -Seconds 2
    Write-Host ('override rows: ' + (Rows 'select FeatureId, TenantId, EmployeeId, IsEnabled from FeatureOverride'))
    Assert-That ((Rows 'select count(*) from FeatureOverride where TenantId=1 and IsEnabled=1') -like '*1*') 'switching the flag wrote the tenant override'
    Assert-That ((Names) -like '*ListItem: Project*') 'Project shows in the menu at once'

    Select-El (Find-Name 'Project')
    Start-Sleep -Seconds 2
    Invoke-El (Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Edit' } | Select-Object -First 1)
    Start-Sleep -Seconds 2
    $withField = Count 'Started On'
    Assert-That ($withField -ge 2) 'the form shows Started On (beside the grid column) while csv-export is on'
    Press 'Cancel'

    Select-El (Find-Name 'Features')
    Start-Sleep -Seconds 2
    Toggle-El (Box 'csv-export')
    Start-Sleep -Seconds 2
    Select-El (Find-Name 'Project')
    Start-Sleep -Seconds 2
    Invoke-El (Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Edit' } | Select-Object -First 1)
    Start-Sleep -Seconds 2
    Assert-That ((Count 'Started On') -lt $withField) 'the form hides Started On while csv-export is off'
    Press 'Cancel'

    Select-El (Find-Name 'Features')
    Start-Sleep -Seconds 2
    Invoke-El (Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Use default' } | Select-Object -First 1)
    Start-Sleep -Seconds 2
    Write-Host ('override rows: ' + (Rows 'select FeatureId, TenantId, EmployeeId, IsEnabled from FeatureOverride'))
} finally { Stop-App }
if ($script:Failed) { exit 1 }
