param([string]$Exe)
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'

function SignIn($name, $password) {
    Start-App -Path $Exe | Out-Null
    Wait-Name 'Sign in' | Out-Null
    Set-Text (Find-Name 'User name') $name
    Set-Text (Find-Name 'Password') $password
    Invoke-El (Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Sign in' } | Select-Object -First 1)
    Start-Sleep -Seconds 4
}

function Press($caption) {
    Invoke-El (Find-Type 'Button' | Where-Object { $_.Current.Name -eq $caption } | Select-Object -First 1)
    Start-Sleep -Milliseconds 1200
}

function EditButtons { @(Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Edit' }) }

# ---- a person who is not an administrator
SignIn 'grace' 'secret-pass-1'
try {
    $menu = (List-Names | Select-Object -First 30) -join ' | '
    Assert-That ($menu -notlike '*Members*') 'a person who is not an administrator has no Members entry'
    Select-El (Find-Name 'Employee')
    Start-Sleep -Seconds 2
    $grid = (List-Names | Select-Object -First 70) -join ' | '
    Assert-That ($grid -notlike '*Administrator?*') 'the list leaves out the administrator column'
    Assert-That ($grid -like '*Manager?*') 'the other columns stay'
    $edits = EditButtons
    Invoke-El $edits[1]   # Grace's own row
    Start-Sleep -Seconds 2
    Assert-That ($null -ne (Find-Name 'Set password')) 'the person can set their own password'
    Assert-That ($null -eq (Find-Like '*Is Administrator*')) 'the administrator flag is not on the form'
    Press 'Cancel'
    $edits = EditButtons
    Invoke-El $edits[0]   # Ada's row
    Start-Sleep -Seconds 2
    Assert-That ($null -eq (Find-Name 'Set password')) 'nobody else can set another person''s password'
    Press 'Cancel'
} finally { Stop-App }

# ---- an administrator
SignIn 'ada' 'secret-pass-1'
try {
    $menu = (List-Names | Select-Object -First 30) -join ' | '
    Assert-That ($menu -like '*Members*') 'an administrator has the Members entry'

    Select-El (Find-Name 'Employee')
    Start-Sleep -Seconds 2
    $grid = (List-Names | Select-Object -First 70) -join ' | '
    Assert-That ($grid -like '*Administrator?*') 'the administrator sees the administrator column'
    $edits = EditButtons
    Invoke-El $edits[1]   # Grace
    Start-Sleep -Seconds 2
    Assert-That ($null -ne (Find-Name 'Set password')) 'an administrator sets anyone''s password'
    Assert-That ($null -ne (Find-Like '*Is Administrator*')) 'the administrator sees the flag on the form'
    Set-Text (Find-Name 'Set password') 'short'
    Press 'Save'
    $now = (List-Names | Select-Object -First 40) -join ' | '
    Assert-That ($now -like '*at least 8 characters*') 'a short password is refused'
    Set-Text (Find-Name 'Set password') 'brand-new-pass-9'
    Press 'Save'
    Start-Sleep -Seconds 2

    Select-El (Find-Name 'Members')
    Start-Sleep -Seconds 2
    Press 'Add member'
    Press 'Add'
    Start-Sleep -Seconds 2
    $members = (List-Names | Select-Object -First 60) -join ' | '
    Write-Host $members
    Assert-That ($members -like '*Alan Turing (alan)*') 'Alan was added to the tenant'
    $roles = @(Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Edit role' })
    Invoke-El $roles[1]
    Start-Sleep -Seconds 1
    Set-Text (@(Find-Type 'Edit' | Where-Object { $_.Current.Name -eq 'Role' })[0]) 'Employee'
    Press 'Save'
    Start-Sleep -Seconds 2
    $members = (List-Names | Select-Object -First 60) -join ' | '
    Assert-That ($members -like '*Alan Turing (alan) | Text: Employee*') 'the role was saved'
    $removes = @(Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Remove' })
    Invoke-El $removes[1]
    Start-Sleep -Seconds 2
    $members = (List-Names | Select-Object -First 60) -join ' | '
    Assert-That ($members -notlike '*Alan Turing*') 'Alan was removed'
    $removes = @(Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Remove' })
    Invoke-El $removes[0]   # Ada removing herself
    Start-Sleep -Seconds 1
    $members = (List-Names | Select-Object -First 60) -join ' | '
    Write-Host $members; Assert-That ($members -like '*cannot remove yourself*') 'a person cannot remove themself'
} finally { Stop-App }

# ---- the new password works
SignIn 'grace' 'brand-new-pass-9'
try {
    Assert-That ($null -eq (Find-Name 'Password')) 'Grace signs in with the password the administrator set'
} finally { Stop-App }
