# Drives a running WinUI3 / WPF app through Windows UI Automation: start it, find elements by their
# accessible name, click, select, read, and always stop it. Import with:  Import-Module .\UiAutomation.psm1 -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$script:UIA = [System.Windows.Automation.AutomationElement]
$script:Process = $null
$script:Window = $null

function New-Condition($property, $value) { New-Object System.Windows.Automation.PropertyCondition($property, $value) }

function Start-App {
    <# Starts the executable and waits (up to $TimeoutSeconds) for its main window; returns the window element. #>
    param([Parameter(Mandatory)][string]$Path, [string[]]$Arguments = @(), [int]$TimeoutSeconds = 30)
    $script:Process = if ($Arguments.Count) { Start-Process $Path -ArgumentList $Arguments -PassThru } else { Start-Process $Path -PassThru }
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 500
        $script:Window = $script:UIA::RootElement.FindFirst('Children', (New-Condition $script:UIA::ProcessIdProperty ([int]$script:Process.Id)))
    } until ($script:Window -or (Get-Date) -gt $deadline -or $script:Process.HasExited)
    if (-not $script:Window) { throw "No window for $Path within $TimeoutSeconds seconds." }
    Start-Sleep -Seconds 2   # let the first page finish loading
    $script:Window
}

function Get-AppWindow {
    <# The app's current top-level window; a content dialog is part of it, but re-read it after a dialog opens or closes. #>
    $script:UIA::RootElement.FindFirst('Children', (New-Condition $script:UIA::ProcessIdProperty ([int]$script:Process.Id)))
}

function Find-Name {
    <# The first descendant whose accessible name is exactly $Name, or null. #>
    param([Parameter(Mandatory)][string]$Name, $Scope = $null)
    if (-not $Scope) { $Scope = Get-AppWindow }
    $Scope.FindFirst('Descendants', (New-Condition $script:UIA::NameProperty $Name))
}

function Find-Like {
    <# Every descendant whose accessible name matches the -like pattern. #>
    param([Parameter(Mandatory)][string]$Pattern, $Scope = $null)
    if (-not $Scope) { $Scope = Get-AppWindow }
    $Scope.FindAll('Descendants', [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -like $Pattern }
}

function Find-Type {
    <# Every descendant of a control type, e.g. Edit, ComboBox, CheckBox, TabItem, ListItem, Button. #>
    param([Parameter(Mandatory)][string]$Type, $Scope = $null)
    if (-not $Scope) { $Scope = Get-AppWindow }
    $Scope.FindAll('Descendants', (New-Condition $script:UIA::ControlTypeProperty ([System.Windows.Automation.ControlType]::$Type)))
}

function Invoke-El($El) { $El.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Select-El($El) { $El.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
function Toggle-El($El) { $El.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
function Expand-El($El) { $El.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand() }

function Set-Text {
    <# Sets a text box's value. A TextBox commits on focus loss, so move focus on afterwards (Send-Keys '{TAB}'). #>
    param([Parameter(Mandatory)]$El, [Parameter(Mandatory)][string]$Text)
    $El.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text)
}

function Get-Text($El) {
    try { $El.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { $El.Current.Name }
}

function Send-Keys([string]$Keys) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
}

function List-Names {
    <# Every descendant as "Type: Name" lines: the first thing to run against an unfamiliar screen. #>
    param($Scope = $null)
    if (-not $Scope) { $Scope = Get-AppWindow }
    $Scope.FindAll('Descendants', [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object {
        "$($_.Current.ControlType.ProgrammaticName.Replace('ControlType.', '')): $($_.Current.Name)"
    }
}

function Wait-Name {
    <# Waits until an element with this name exists (or fails after $TimeoutSeconds); returns it. #>
    param([Parameter(Mandatory)][string]$Name, [int]$TimeoutSeconds = 20, $Scope = $null)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $el = Find-Name $Name $Scope
        if ($el) { return $el }
        Start-Sleep -Milliseconds 500
    } until ((Get-Date) -gt $deadline)
    throw "Timed out waiting for '$Name'."
}

function Stop-App { if ($script:Process -and -not $script:Process.HasExited) { $script:Process.Kill() } }

function Assert-That {
    <# A check that prints PASS / FAIL and sets the exit code, so a script can be a pass/fail run. #>
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Message)
    if ($Condition) { Write-Host "PASS  $Message" -ForegroundColor Green }
    else { Write-Host "FAIL  $Message" -ForegroundColor Red; $global:LASTEXITCODE = 1; $script:Failed = $true }
}

Export-ModuleMember -Function *
