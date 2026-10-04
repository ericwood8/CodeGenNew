<#
.SYNOPSIS  Starts a generated API against a (scratch) database and walks one route through create, read, update, list and delete.
.DESCRIPTION
  The connection string is passed through the environment (ConnectionStrings__DbConnectionString) so no file is edited; it must not contain a password. PostgreSQL's driver
  reads PGPASSWORD and MySQL's MYSQL_PWD only if the connection string leaves it out: set those in your own shell. The API is started on a spare port and always stopped.
  Run it against a copy made by ScratchDatabase.ps1, never the real database.
.EXAMPLE
  .\Test-ApiCrud.ps1 -ApiProject C:\InvoiceSystemPg\InvoiceSystem.Api -Route /api/customers `
      -ConnectionString 'Host=localhost;Port=5432;Database=InvoiceSystem_scratch;Username=dev_login' `
      -Body '{"accountNumber":"VERIFY-1","customerName":"Verification Co","customerStatusId":1,"isTaxable":false}' -IdProperty customerId
#>
param(
    [Parameter(Mandatory)][string]$ApiProject,
    [Parameter(Mandatory)][string]$Route,
    [Parameter(Mandatory)][string]$ConnectionString,
    [Parameter(Mandatory)][string]$Body,
    [string]$IdProperty = 'id',
    [string]$UpdateProperty,
    [string]$UpdateValue = 'Changed by verification',
    [int]$Port = 5199
)
Import-Module (Join-Path $PSScriptRoot 'UiAutomation.psm1') -Force -WarningAction Ignore   # only for Assert-That
$script:Failed = $false
$base = "http://localhost:$Port"
$env:ConnectionStrings__DbConnectionString = $ConnectionString
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$api = Start-Process dotnet -ArgumentList @('run', '--project', $ApiProject, '--no-launch-profile', '--urls', $base) -PassThru -WindowStyle Hidden

try {
    $deadline = (Get-Date).AddSeconds(90)
    $up = $false
    do {
        Start-Sleep -Seconds 2
        try { Invoke-WebRequest "$base$Route" -UseBasicParsing -TimeoutSec 5 | Out-Null; $up = $true } catch { if ($_.Exception.Response) { $up = $true } }
    } until ($up -or (Get-Date) -gt $deadline -or $api.HasExited)
    Assert-That $up "the API answers on $base"
    if (-not $up) { throw "The API did not start (the process $(if ($api.HasExited) { 'exited with ' + $api.ExitCode } else { 'is running' }))." }

    $created = Invoke-RestMethod "$base$Route" -Method Post -Body $Body -ContentType 'application/json'
    $id = $created.$IdProperty
    Assert-That ($null -ne $id -and $id -ne 0) "POST creates a row (id $id)"

    $read = Invoke-RestMethod "$base$Route/$id"
    Assert-That ($null -ne $read) 'GET by id returns it'

    if ($UpdateProperty) {
        $read.$UpdateProperty = $UpdateValue
        Invoke-RestMethod "$base$Route/$id" -Method Put -Body ($read | ConvertTo-Json -Depth 5) -ContentType 'application/json' | Out-Null
        $again = Invoke-RestMethod "$base$Route/$id"
        Assert-That ($again.$UpdateProperty -eq $UpdateValue) "PUT changed $UpdateProperty"
    }

    $all = Invoke-RestMethod "$base$Route"
    Assert-That (@($all).Count -ge 1) 'GET all lists rows'

    Invoke-RestMethod "$base$Route/$id" -Method Delete | Out-Null
    $gone = $false
    try { Invoke-RestMethod "$base$Route/$id" | Out-Null } catch { $gone = $_.Exception.Response.StatusCode -eq 404 }
    Assert-That $gone 'DELETE removes it (GET is 404)'
}
finally {
    if ($api -and -not $api.HasExited) { taskkill /PID $api.Id /T /F | Out-Null }   # the whole tree: dotnet run starts the API as a child
}
if ($script:Failed) { exit 1 }
