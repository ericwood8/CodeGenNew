param([string]$Exe, [string]$Db)
# Clicks through a generated WinUI3 or Uno (Windows target) app over a scratch SQLite copy of Schemas/features.sql generated with EditableChildren=Employee.EmployeeTenant and
# FeatureFields=Project.StartedOn:csv-export,EmployeeTenant.Role:csv-export (seed the sign-ins first; the Uno app needs the API running).
# The Employee dialog's editable rows show Role and save a change while csv-export is on; with it off the cell is not drawn and a save of another field keeps the stored Role;
# The app reads the flags at sign-in (no FeatureRefreshSeconds), so it is started again after the override changes.
# a Project save with Started On hidden keeps its stored date.
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'

function Rows($sql) { (python -c "import sqlite3,sys; print(sqlite3.connect(sys.argv[1]).execute(sys.argv[2]).fetchall())" $Db $sql) }
function Run($sql) { python -c "import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute(sys.argv[2]); c.commit()" $Db $sql }
function Press($caption) { Invoke-El (Find-Type 'Button' | Where-Object { $_.Current.Name -eq $caption } | Select-Object -First 1); Start-Sleep -Milliseconds 1500 }
function Count($text) { @(List-Names | Where-Object { $_ -like "*$text*" }).Count }
function Box($name) { Find-Type 'Edit' | Where-Object { $_.Current.Name -eq $name } | Select-Object -First 1 }
function OpenEdit($entry) { Select-El (Find-Name $entry); Start-Sleep -Seconds 2; Press 'Edit'; Start-Sleep -Seconds 2 }

Run 'delete from FeatureOverride'
Run "update EmployeeTenant set Role = 'Lead' where EmployeeTenantId = 1"
Run "update Project set Name = 'Apollo', StartedOn = '2026-03-04' where ProjectId = 1"
Run 'insert into FeatureOverride (FeatureId, TenantId, IsEnabled) values (1, 1, 1)'
function SignIn {
    Start-App -Path $Exe | Out-Null
    Wait-Name 'Sign in' | Out-Null
    Set-Text (Find-Name 'User name') 'ada'
    Set-Text (Find-Name 'Password') 'secret-pass'
    Invoke-El (Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Sign in' } | Select-Object -First 1)
    Start-Sleep -Seconds 4
}
SignIn
try {

    OpenEdit 'Employee'
    $roleOn = Count 'Role'
    Write-Host ('Role names with csv-export on: ' + $roleOn)
    Assert-That ($roleOn -ge 2) 'the editable rows draw the Role header and cell while csv-export is on'
    Set-Text (Box 'Role') 'Chief'
    Press 'Save'
    Start-Sleep -Seconds 2
    Assert-That ((Rows 'select Role from EmployeeTenant where EmployeeTenantId = 1') -like "*Chief*") 'a changed Role in the editable rows was saved'

    Stop-App
    Run 'insert into FeatureOverride (FeatureId, TenantId, IsEnabled) values (2, 1, 0)'
    SignIn
    OpenEdit 'Employee'
    $roleOff = Count 'Role'
    Write-Host ('Role names with csv-export off: ' + $roleOff)
    Assert-That ($roleOff -lt $roleOn) 'the editable rows leave out the Role cell while csv-export is off'
    Set-Text (Box 'Full Name') 'Ada Byron'
    Press 'Save'
    Start-Sleep -Seconds 2
    Assert-That ((Rows 'select FullName from Employee where EmployeeId = 1') -like '*Ada Byron*') 'the Employee was saved with the cell hidden'
    Assert-That ((Rows 'select Role from EmployeeTenant where EmployeeTenantId = 1') -like '*Chief*') 'the hidden cell kept its stored Role'

    OpenEdit 'Project'
    Assert-That ((Count 'Started On') -eq 0) 'the Project form leaves out Started On while csv-export is off'
    foreach ($name in @(Find-Type 'Edit' | Where-Object { $_.Current.Name -eq 'Name' })) { Set-Text $name 'Apollo Two' }   # the list's filter box has the same name; the order of the two differs between targets
    (Find-Type 'Edit' | Where-Object { $_.Current.Name -eq 'Description' } | Select-Object -Last 1).SetFocus()   # a text box writes its value back when it loses focus
    Press 'Save'
    Start-Sleep -Seconds 2
    $project = Rows 'select Name, StartedOn from Project where ProjectId = 1'
    Write-Host $project
    Assert-That ($project -like '*Apollo Two*') 'the Project was saved'
    Assert-That ($project -like '*2026-03-04*') 'the hidden Started On kept its stored date'
} finally { Stop-App }
Run 'delete from FeatureOverride'
if ($script:Failed) { exit 1 }
