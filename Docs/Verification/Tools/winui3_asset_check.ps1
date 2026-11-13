param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Work, [string]$Menu = 'Asset')
# Clicks through the Asset screen of a generated WinUI3 or Uno (Windows target) app over a scratch copy of Schemas/assets.sql generated with
# AssetTables=Asset:StorageKey:FileName:MimeType:SizeBytes:PublicUrl and AssetAllowedTypes=image/png,text/plain, with UI Automation and the real Windows file dialogs:
#   powershell -File winui3_asset_check.ps1 -Exe <app.exe> -Work <folder holding pixel.png and note.txt>
# (the Uno app needs the API running and API_BASE_URL set). It presses Upload on the list and types the path into the Open dialog, presses Download on the row and types a path into the Save dialog,
# opens the row's edit dialog (Download, and the picture), downloads from it, then adds a row from the Add dialog's own Upload button. The files it saves go to <Work>\saved-*.
# Read the rows back from the database afterwards (asset_desktop_check.py does it); the script only writes through the app.
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'
$ui = [System.Windows.Automation.AutomationElement]

function Count-Buttons([string]$name, $scope = $null) { @(Find-Type 'Button' $scope | Where-Object { $_.Current.Name -eq $name }).Count }
function First-Button([string]$name, $scope = $null) { @(Find-Type 'Button' $scope | Where-Object { $_.Current.Name -eq $name })[0] }

function Use-Picker([int]$processId, [string]$path) {
    # the file dialog is a top-level window of the app with a "File name" box; type the path there and press its default button
    $deadline = (Get-Date).AddSeconds(25)
    $picker = $null
    $box = $null
    do {
        Start-Sleep -Milliseconds 500
        $windows = $ui::RootElement.FindAll('Children', (New-Object System.Windows.Automation.PropertyCondition($ui::ProcessIdProperty, $processId)))
        foreach ($w in $windows) {
            # the Open dialog has a "File name" combo box with id 1148; the Save As dialog is a window named Save As
            $box = $w.FindFirst('Descendants', (New-Object System.Windows.Automation.OrCondition(
                (New-Object System.Windows.Automation.PropertyCondition($ui::AutomationIdProperty, '1148')),
                (New-Object System.Windows.Automation.PropertyCondition($ui::NameProperty, 'Save As')))))
            if ($box) { $picker = $w; break }
        }
    } until ($picker -or (Get-Date) -gt $deadline)
    if (-not $picker) { throw 'No file dialog appeared.' }
    # the dialog opens with the cursor in its "File name" box (UI Automation cannot set or focus that box): type the path and press Enter
    Start-Sleep -Milliseconds 800
    Send-Keys $path
    Start-Sleep -Milliseconds 500
    Send-Keys '{ENTER}'
    Start-Sleep -Seconds 3
}

$window = Start-App -Path $Exe
$processId = $window.Current.ProcessId
try {
    Start-Sleep -Seconds 5
    Select-El (Find-Name $Menu)
    Start-Sleep -Seconds 4
    Assert-That ((Count-Buttons 'Upload') -ge 1) 'the list has an Upload button'
    $before = Count-Buttons 'Download'

    Invoke-El (First-Button 'Upload')
    Use-Picker $processId (Join-Path $Work 'pixel.png')
    Start-Sleep -Seconds 3
    Assert-That ((Count-Buttons 'Download') -eq ($before + 1)) 'the file chosen in the Open dialog became a row with a Download button'

    Invoke-El (First-Button 'Download')
    Use-Picker $processId (Join-Path $Work 'saved-from-row.png')
    Assert-That (Test-Path (Join-Path $Work 'saved-from-row.png')) 'Download on the row wrote the file the Save dialog named'

    Invoke-El (First-Button 'Edit')
    Start-Sleep -Seconds 4
    $dialog = Wait-Name 'Edit Asset'
    $scope = Get-AppWindow
    $names = (List-Names $scope) -join ' | '
    Assert-That ((Count-Buttons 'Download' $dialog) -ge 1 -or $names -match 'Button: Download') 'the edit dialog has a Download button'
    $images = @(Find-Type 'Image' $scope).Count
    Write-Host "      images in the window: $images"
    Assert-That ($images -ge 1) 'the edit dialog shows a picture'
    $buttons = @(Find-Type 'Button' $scope | Where-Object { $_.Current.Name -eq 'Download' })
    Invoke-El $buttons[$buttons.Count - 1]
    Use-Picker $processId (Join-Path $Work 'saved-from-dialog.png')
    Assert-That (Test-Path (Join-Path $Work 'saved-from-dialog.png')) 'Download in the dialog wrote the file the Save dialog named'
    Invoke-El (First-Button 'Cancel')
    Start-Sleep -Seconds 2

    $rows = Count-Buttons 'Download'
    Invoke-El (First-Button 'Add New')
    Start-Sleep -Seconds 3
    $dialog = Wait-Name 'Add Asset'
    $scope = Get-AppWindow
    $inDialogUpload = @(Find-Type 'Button' $scope | Where-Object { $_.Current.Name -eq 'Upload' })
    Assert-That ($inDialogUpload.Count -ge 2) 'the Add dialog has its own Upload button'
    Invoke-El $inDialogUpload[$inDialogUpload.Count - 1]
    Use-Picker $processId (Join-Path $Work 'note.txt')
    Start-Sleep -Seconds 4
    # the list is read again only once the dialog has closed, so a new row in it shows the dialog closed
    Assert-That ((Count-Buttons 'Download') -eq ($rows + 1)) 'the list shows the row the dialog made'
}
catch {
    # a picture of the screen shows what the app was doing when a step failed
    Add-Type -AssemblyName System.Windows.Forms, System.Drawing
    $area = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = New-Object System.Drawing.Bitmap $area.Width, $area.Height
    [System.Drawing.Graphics]::FromImage($bitmap).CopyFromScreen($area.Location, [System.Drawing.Point]::Empty, $area.Size)
    $bitmap.Save((Join-Path $Work 'failure.png'))
    Write-Host "      app still running: $(-not (Get-Process -Id $processId -ErrorAction SilentlyContinue) -eq $false)"
    throw
}
finally {
    Stop-App
}
if ($script:Failed) { exit 1 }
