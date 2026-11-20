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

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class UiaMouse
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(int flags, int dx, int dy, int data, int extra);
    public static void Click(int x, int y) { SetCursorPos(x, y); mouse_event(2, 0, 0, 0, 0); mouse_event(4, 0, 0, 0, 0); }
    public static void RightClick(int x, int y) { SetCursorPos(x, y); mouse_event(8, 0, 0, 0, 0); mouse_event(16, 0, 0, 0, 0); }
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    // a move the input system sees as hardware input (SetCursorPos alone moves the cursor but raises no pointer event for the app)
    public static void Move(int x, int y)
    {
        int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
        mouse_event(0x8001, x * 65535 / (w - 1), y * 65535 / (h - 1), 0, 0);
    }
    public static void Down() { mouse_event(2, 0, 0, 0, 0); }
    public static void Up() { mouse_event(4, 0, 0, 0, 0); }
}
'@ -ErrorAction SilentlyContinue

function Click-Point {
    <# A real mouse click at a screen point (the window must be in front). -Right for a right click. #>
    param([Parameter(Mandatory)][int]$X, [Parameter(Mandatory)][int]$Y, [switch]$Right)
    if ($Right) { [UiaMouse]::RightClick($X, $Y) } else { [UiaMouse]::Click($X, $Y) }
}

function Drag-Point {
    <# Presses the left button at one screen point, moves in steps to another and releases (the right edge of a header dragged to resize a column of a table). #>
    param([Parameter(Mandatory)][int]$FromX, [Parameter(Mandatory)][int]$FromY, [Parameter(Mandatory)][int]$ToX, [Parameter(Mandatory)][int]$ToY, [int]$Steps = 15)
    [UiaMouse]::Move($FromX, $FromY); Start-Sleep -Milliseconds 400
    [UiaMouse]::Move($FromX, $FromY); Start-Sleep -Milliseconds 200
    [UiaMouse]::Down(); Start-Sleep -Milliseconds 200
    for ($i = 1; $i -le $Steps; $i++) {
        [UiaMouse]::Move([int]($FromX + ($ToX - $FromX) * $i / $Steps), [int]($FromY + ($ToY - $FromY) * $i / $Steps)); Start-Sleep -Milliseconds 40
    }
    [UiaMouse]::Up(); Start-Sleep -Milliseconds 300
}

function Click-GridHeader {
    <# Clicks the header of a column of a generated WinUI3 table (WinUI3Grid=TableView). The table's column headers are not in the UI Automation tree, but its cells are named
       '<column>, Row N, <value>' and sit under their header, so the click goes at the first cell's centre across and the table's top edge down (the header row). -Right for a right click. #>
    param([Parameter(Mandatory)][string]$Column, [string]$GridName, [switch]$Right)
    $grid = if ($GridName) { Find-Name $GridName } else { Find-Type 'DataGrid' | Select-Object -First 1 }
    if (-not $grid) { throw 'No table on the page.' }
    $cell = Find-Like "$Column, Row 1, *" | Select-Object -First 1
    if (-not $cell) { $cell = Find-Like "$Column, Row 1" | Select-Object -First 1 }
    if (-not $cell) { throw "No cell of column '$Column' in the first row." }
    $cellRect = $cell.Current.BoundingRectangle
    $gridRect = $grid.Current.BoundingRectangle
    Click-Point -X ([int]($cellRect.X + $cellRect.Width / 2)) -Y ([int]($gridRect.Y + 25)) -Right:$Right
}

function Stop-App { if ($script:Process -and -not $script:Process.HasExited) { $script:Process.Kill() } }

function Assert-That {
    <# A check that prints PASS / FAIL and sets the exit code, so a script can be a pass/fail run. #>
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Message)
    if ($Condition) { Write-Host "PASS  $Message" -ForegroundColor Green }
    else { Write-Host "FAIL  $Message" -ForegroundColor Red; $global:LASTEXITCODE = 1; $script:Failed = $true }
}

Export-ModuleMember -Function *
