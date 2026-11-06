param([string]$Exe)
# Clicks through the usage page of a generated WinUI3 or Uno (Windows target) app over a scratch SQLite copy of Schemas/usage_decimal.sql (the Uno app needs the API running).
# Settings: Auth, ApiAuth, TenantTable, TenantMembership, UserTable, SubscriptionTable, UsageTables=UsageEvent, UsageRollup=UsageMonthly:UsageMonth:Quantity.
# Seed the sign-ins and the events first (the hash of usage_check.py; five events and one monthly row). Build the app with: dotnet build -p:Platform=x64 (WinUI3) or -f net10.0-desktop (Uno).
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'

function Names { (List-Names | Select-Object -First 150) -join ' | ' }
function Press($caption) { Invoke-El (Find-Type 'Button' | Where-Object { $_.Current.Name -eq $caption } | Select-Object -First 1); Start-Sleep -Milliseconds 1500 }

Start-App -Path $Exe | Out-Null
try {
    Wait-Name 'Sign in' | Out-Null
    Set-Text (Find-Name 'User name') 'ada'
    Set-Text (Find-Name 'Password') 'secret-pass'
    Invoke-El (Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Sign in' } | Select-Object -First 1)
    Start-Sleep -Seconds 4

    $menu = Names
    Write-Host $menu
    Assert-That ($menu -like '*Usage Events*') 'the administrator has the Usage Events entry'

    Select-El (Find-Name 'Usage Events')
    Start-Sleep -Seconds 3
    $page = Names
    Write-Host $page
    Assert-That ($page -like '*2026-11*api_calls*') 'the page lists this month'
    Assert-That ($page -like '*2026-08*') 'the page lists the rolled up month'
    Assert-That ($page -like '*250*') 'the rolled up month shows its total'
    Assert-That ($page -notlike '*99*') 'another tenant is not shown'

    Set-Text (Find-Name 'Kind') 'emails'
    Press 'Show'
    $filtered = Names
    Write-Host $filtered
    Assert-That ($filtered -like '*emails*' -and $filtered -notlike '*api_calls*') 'the filter keeps one kind'

    Set-Text (Find-Name 'Kind') ' '
    Set-Text (Find-Name 'From') '2026-11-01'
    Set-Text (Find-Name 'Before') '2026-11-03'
    Press 'Show'
    $ranged = Names
    Write-Host $ranged
    Assert-That ($ranged -like '*2026-11*api_calls*12.5*' -and $ranged -notlike '*emails*' -and $ranged -notlike '*2026-10*') 'From and Before keep the days between them'
    Assert-That ($ranged -like '*ProgressBar*') 'each line has a bar'

    Set-Text (Find-Name 'From') '2026-10-01'
    Set-Text (Find-Name 'Before') ' '
    Press 'Show'
    $later = Names
    Assert-That ($later -like '*2026-10*' -and $later -notlike '*2026-08*') 'From drops the rolled up month that starts before it'

    Set-Text (Find-Name 'From') 'banana'
    Press 'Show'
    Assert-That ((Names) -like '*written year-month-day*') 'a day that is not a day is explained'
} finally { Stop-App }
if ($script:Failed) { exit 1 }
