param([string]$Exe, [string]$WebhookMenu = 'Webhook Event', [string]$ErrorMenu = 'Error Report', [int]$WebhookTotal = 3, [int]$WebhookWaiting = 2, [int]$ErrorTotal = 2, [int]$ErrorWaiting = 1)
# Clicks through the list screens of the inbound landing tables in a generated WinUI3 or Uno (Windows target) app over a scratch copy of Schemas/inbound.sql (the Uno app needs the API running):
#   powershell -File winui3_inbound_check.ps1 -Exe <app.exe>
# Seed three webhook events (two waiting) and two error reports (one waiting) first, or say how many rows there are with -WebhookTotal, -WebhookWaiting, -ErrorTotal and -ErrorWaiting and the names of the menu entries with -WebhookMenu and -ErrorMenu. Checks the summary sentence, the Processed button on the waiting rows only, and that a click
# brings the sentence and the buttons up to date. Read the rows back from the database afterwards (the script does not touch it).
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'

function Names { (List-Names | Select-Object -First 200) -join ' | ' }
function Processed-Buttons { @(Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Processed' }) }
function Summary { (List-Names | Where-Object { $_ -like 'Text: Unprocessed:*' } | Select-Object -First 1) }

Start-App -Path $Exe | Out-Null
try {
    Start-Sleep -Seconds 5
    Select-El (Find-Name $WebhookMenu)
    Start-Sleep -Seconds 4
    Write-Host (Summary)
    Assert-That ((Summary) -like "*Unprocessed: $WebhookWaiting of $WebhookTotal. Oldest unprocessed: #*Senders with a secret of their own: stripe. Collect route: on, 30 a minute.*") 'the webhook list says how many events wait'
    Assert-That ((Processed-Buttons).Count -eq $WebhookWaiting) 'the waiting events have a Processed button and the finished ones have none'

    Invoke-El (Processed-Buttons)[0]
    Start-Sleep -Seconds 4
    Write-Host (Summary)
    Assert-That ((Summary) -like "*Unprocessed: $($WebhookWaiting - 1) of $WebhookTotal.*") 'the sentence is up to date after Processed'
    Assert-That ((Processed-Buttons).Count -eq ($WebhookWaiting - 1)) 'the processed event lost its button'

    Select-El (Find-Name $ErrorMenu)
    Start-Sleep -Seconds 4
    Write-Host (Summary)
    Assert-That ((Summary) -like "*Unprocessed: $ErrorWaiting of $ErrorTotal. Oldest unprocessed: #*") 'the error list says how many reports wait'
    Assert-That ((Processed-Buttons).Count -eq $ErrorWaiting) 'the waiting reports have a Processed button'

    Invoke-El (Processed-Buttons)[0]
    Start-Sleep -Seconds 4
    Write-Host (Summary)
    $left = $ErrorWaiting - 1
    if ($left -eq 0) { Assert-That ((Summary) -like "*Unprocessed: 0 of $ErrorTotal. Senders*") 'no report waits and no oldest is named' }
    else { Assert-That ((Summary) -like "*Unprocessed: $left of $ErrorTotal. Oldest unprocessed: #*") 'the sentence is up to date after Processed' }
    Assert-That ((Processed-Buttons).Count -eq $left) 'the processed report lost its button'
} finally { Stop-App }
if ($script:Failed) { exit 1 }
