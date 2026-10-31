param([string]$Exe)
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'
try {
    Start-App -Path $Exe | Out-Null
    Wait-Name 'Create the first administrator' | Out-Null
    Write-Host 'offer shown'
    Assert-That ($null -ne (Find-Name 'Confirm password')) 'a confirm box is shown'
    Assert-That ($null -eq (Find-Name 'Tenant')) 'the tenant drop-down is hidden while creating the first person'

    # a short password is refused with the reason
    Set-Text (Find-Name 'User name') 'owner'
    Set-Text (Find-Name 'Password') 'short'
    Set-Text (Find-Name 'Confirm password') 'short'
    Invoke-El (Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Create and sign in' } | Select-Object -First 1)
    Start-Sleep -Seconds 1
    $names = (List-Names | Select-Object -First 40) -join ' | '
    Assert-That ($names -like '*at least 8 characters*') 'a short password is refused'

    # two different passwords are refused
    Set-Text (Find-Name 'Password') 'long-enough-1'
    Set-Text (Find-Name 'Confirm password') 'long-enough-2'
    Invoke-El (Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Create and sign in' } | Select-Object -First 1)
    Start-Sleep -Seconds 1
    $names = (List-Names | Select-Object -First 40) -join ' | '
    Assert-That ($names -like '*not the same*') 'different passwords are refused'

    # the right values create the person and sign in
    Set-Text (Find-Name 'Password') 'long-enough-1'
    Set-Text (Find-Name 'Confirm password') 'long-enough-1'
    Invoke-El (Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Create and sign in' } | Select-Object -First 1)
    Start-Sleep -Seconds 4
    $after = (List-Names | Select-Object -First 60) -join ' | '
    Write-Host $after
    Assert-That ($null -eq (Find-Name 'Password')) 'the sign-in view is gone: signed in'
    Assert-That ($after -like '*Members*') 'the first person is an administrator: the Members entry is in the menu'
} finally { Stop-App }
