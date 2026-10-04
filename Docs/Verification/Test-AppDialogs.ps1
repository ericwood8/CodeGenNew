<#
.SYNOPSIS  Click-through of the CodeGenNew app's dialogs that need no database: the Project settings dialog and the Essentials dialog.
.DESCRIPTION
  Starts the built app, opens each dialog through its toolbar button, checks that the expected tabs / groups / buttons are there, closes it and stops the app.
  The exit code is 1 when any check failed. Build the app first:  dotnet build CodeGenNew.App -c Debug -p:Platform=x64
.EXAMPLE
  .\Test-AppDialogs.ps1
#>
param([string]$Exe = (Join-Path $PSScriptRoot '..\..\CodeGenNew.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\CodeGenNew.exe'))
Import-Module (Join-Path $PSScriptRoot 'UiAutomation.psm1') -Force
$script:Failed = $false
$Exe = (Resolve-Path $Exe).Path

try {
    Start-App $Exe | Out-Null

    # --- the Project settings dialog: tabs per stack, settings as text boxes
    Invoke-El (Wait-Name 'Project')
    $general = Wait-Name 'General'
    Assert-That ($null -ne $general) 'the Project dialog opens on a General tab'
    $tabs = @(Find-Type 'TabItem' | ForEach-Object { $_.Current.Name })
    foreach ($tab in 'General', 'WinUI3') {
        Assert-That ($tabs -contains $tab) "the Project dialog has a '$tab' tab"
    }
    Select-El (Find-Name 'WinUI3')
    Start-Sleep -Seconds 1
    Assert-That (@(Find-Type 'Edit').Count -gt 0) 'the WinUI3 tab shows text boxes'
    Select-El (Find-Name 'General')
    Invoke-El (Find-Name 'Cancel')
    Start-Sleep -Seconds 1

    # --- the Essentials dialog: a group list, a replace switch, nothing written until Generate
    Invoke-El (Wait-Name 'Essentials')
    Invoke-El (Wait-Name 'API essentials')
    $title = Wait-Name 'File groups'
    Assert-That ($null -ne $title) 'the Essentials dialog lists the file groups'
    Assert-That ($null -ne (Find-Like 'Replace files that already exist*')) 'the Essentials dialog has the Replace switch'
    Assert-That (@(Find-Type 'CheckBox').Count -ge 3) 'the API essentials offer several groups'
    Invoke-El (Find-Name 'Close')
}
finally {
    Stop-App
}
if ($script:Failed) { exit 1 }
