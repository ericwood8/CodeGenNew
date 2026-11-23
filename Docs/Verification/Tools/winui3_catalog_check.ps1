param([Parameter(Mandatory)][string]$Exe, [string]$Menu = 'Product')
# Clicks through the bar of a product list (FacetTables=ProductAttribute) in a generated WinUI3 or Uno (Windows target) app over a scratch copy of Schemas/catalog.sql, with UI Automation:
#   powershell -File winui3_catalog_check.ps1 -Exe <app.exe>      (the Uno app needs the API running and API_BASE_URL set)
# Checks that Find products answers the number of products and the first of them, that the Narrow by menu lists the values with their counts, that choosing one narrows the answer and that Clear filters empties it.
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'

function Set-Box([string]$name, [string]$value) {
    $box = Find-Name $name
    $box.SetFocus()
    Start-Sleep -Milliseconds 400
    Set-Text $box $value
}

function Answer {
    $all = (List-Names | Select-Object -First 800) -join ' | '
    if ($all -match 'Text: (\d+ products[^|]*)') { $Matches[1].Trim() } else { '' }
}

function Press([string]$caption) {
    (Find-Name $caption).SetFocus()   # a text box writes its value back when it loses focus
    Invoke-El (Find-Name $caption)
    Start-Sleep -Seconds 3
    Answer
}

Start-App -Path $Exe | Out-Null
try {
    Start-Sleep -Seconds 5
    Select-El (Find-Name $Menu)
    Start-Sleep -Seconds 4
    Set-Box 'Catalog keyword' ' romex '
    $answer = Press 'Find products'
    Assert-That ($answer -match '^3 products: NM-B-14/2 - Building Wire NM-B Romex 14-2 AWG Copper White 100 Ft; NM-B-12/2') "romex finds three products and lists them ($answer)"
    Invoke-El (Find-Name 'Narrow by')
    Start-Sleep -Seconds 2
    $names = (List-Names | Select-Object -First 800) -join ' | '
    Assert-That ($names.Contains('Color: Blue (1)') -and $names.Contains('Material: Copper (3)')) "the menu lists the values with their counts"
    $item = Find-Name 'Color: Blue (1)'
    try { Invoke-El $item } catch { Toggle-El $item }
    Start-Sleep -Seconds 3
    $answer = Answer
    Assert-That ($answer -match '^1 products: NM-B-14/3 - Building Wire NM-B Romex 14-3 AWG Copper Blue 250 Ft$') "choosing Blue leaves one product ($answer)"
    Invoke-El (Find-Name 'Clear filters')
    Start-Sleep -Seconds 2
    Assert-That ((Answer) -eq '') "Clear filters empties the answer"
} finally { Stop-App }
if ($script:Failed) { exit 1 }
