<#
.SYNOPSIS  Checks that every route the generated openapi.yaml documents really exists on the running API, and that the schema's property names are the JSON the API sends.
.DESCRIPTION
  Apply the generated SQL (the search routines) to the scratch database first, or every search path answers 500.
  API_OpenApi writes its paths by the same rules as the API templates, not by reading the API, so the two can drift. This starts the API on a spare port (against a scratch database; the
  connection string goes through the environment and must not hold a password), reads the documented paths, and for each:
    GET  path (a list or a search)  -> 200
    GET  path/{id} with id 1       -> 200 or 404 (a missing row)
    PUT  path/{id} with a body whose key differs -> 400 (the route exists; a missing route is 404)
  and compares the property names of the first row of every list with the schema's properties. The API is always stopped.
.EXAMPLE
  .\Test-OpenApiRoutes.ps1 -ApiProject C:\MySample\My.Api -ConnectionString 'Host=localhost;Port=5432;Database=MyDatabase_scratch;Username=dev_login'
#>
param(
    [Parameter(Mandatory)][string]$ApiProject,
    [Parameter(Mandatory)][string]$ConnectionString,
    [string]$OpenApiFile = (Join-Path $ApiProject 'openapi.yaml'),
    [int]$Port = 5199
)
Import-Module (Join-Path $PSScriptRoot 'UiAutomation.psm1') -Force -WarningAction Ignore   # only for Assert-That
$script:Failed = $false
$base = "http://localhost:$Port"
$env:ConnectionStrings__DbConnectionString = $ConnectionString
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$api = Start-Process dotnet -ArgumentList @('run', '--project', $ApiProject, '--no-launch-profile', '--urls', $base) -PassThru -WindowStyle Hidden

function Get-Status($url, $method = 'Get', $body = $null) {
    try {
        $args = @{ Uri = $url; Method = $method; UseBasicParsing = $true }
        if ($body) { $args.Body = $body; $args.ContentType = 'application/json' }
        (Invoke-WebRequest @args).StatusCode
    } catch { if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 } }
}

try {
    $yaml = Get-Content $OpenApiFile -Raw
    $paths = [regex]::Matches($yaml, '(?m)^  (/api/[^:]+):') | ForEach-Object { $_.Groups[1].Value }
    Assert-That ($paths.Count -gt 0) "the document has $($paths.Count) paths"

    $deadline = (Get-Date).AddSeconds(90)
    do { Start-Sleep -Seconds 2; $up = (Get-Status "$base$($paths[0])") -ne 0 } until ($up -or (Get-Date) -gt $deadline -or $api.HasExited)
    Assert-That $up "the API answers on $base"
    if (-not $up) { throw 'The API did not start.' }

    foreach ($path in $paths) {
        if ($path -match '/clone$') { continue }   # a POST that would add a row: left to Test-ApiCrud.ps1
        $url = "$base" + ($path -replace '\{id\}', '1')
        if ($path -match '/search$') { $url += '?pageNumber=1&pageSize=2' }
        $status = Get-Status $url
        if ($path -match '\{id\}$') {
            Assert-That ($status -in 200, 404) "GET $path answers $status"
            $put = Get-Status $url 'Put' '{"id":-1}'
            Assert-That ($put -eq 400) "PUT $path answers $put (400: the route exists)"
        } else {
            Assert-That ($status -eq 200) "GET $path answers $status"
        }
    }

    # the schema property names against the JSON of a real row
    foreach ($path in $paths | Where-Object { $_ -notmatch '\{id\}|/search$' }) {
        $json = (Invoke-WebRequest "$base$path" -UseBasicParsing).Content | ConvertFrom-Json -NoEnumerate
        if (@($json).Count -eq 0) { continue }
        $keys = @($json[0].PSObject.Properties.Name)
        $name = (Get-Culture).TextInfo.ToTitleCase(($path -split '/')[-1])
        $schema = [regex]::Matches($yaml, '(?ms)^    (\w+):\r?\n      type: object.*?(?=^    \w+:\r?\n|\z)') | Where-Object { $_.Groups[1].Value -ieq $name -or $_.Groups[1].Value -ieq ($name -replace 's$', '') -or $_.Groups[1].Value -ieq ($name -replace 'ies$', 'y') } | Select-Object -First 1
        if (-not $schema) { continue }
        $props = @([regex]::Matches($schema.Value, '(?m)^        (\w+):\s*$') | ForEach-Object { $_.Groups[1].Value })
        $missing = @($props | Where-Object { $keys -notcontains $_ })
        Assert-That ($missing.Count -eq 0) "$($schema.Groups[1].Value): every schema property is in the JSON$(if ($missing) { ' (missing: ' + ($missing -join ', ') + ')' })"
    }
}
finally {
    if ($api -and -not $api.HasExited) { taskkill /PID $api.Id /T /F | Out-Null }
}
if ($script:Failed) { exit 1 }
