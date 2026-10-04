<#
.SYNOPSIS  Generates the API of a SQLite database, builds it, and walks one route through create, read, update, list and delete.
.DESCRIPTION
  SQLite is a file, so the check needs no server: it reads the database you name (never writes to it), generates a project into a temporary folder with codegen's `generate`, builds it, copies the
  database beside the API as a scratch file and runs Test-ApiCrud.ps1 against that copy. The project file (CODEGENNEW_HOME\Projects\<Project>.config) must say DatabaseProvider=Sqlite and
  OutputApi=<folder>; the API folder is found by that name. Run it from the repository root after `dotnet build CodeGenNew.Cli`.
.EXAMPLE
  .\Docs\Verification\Test-SqliteStack.ps1 -Database C:\data\shop.db -Project Shop -ApiFolder Shop.Api -Route /api/customers `
      -Body '{"name":"Verify Co","status":"Active","isTaxable":false}' -IdProperty customerId
#>
param(
    [Parameter(Mandatory)][string]$Database,
    [Parameter(Mandatory)][string]$Project,
    [Parameter(Mandatory)][string]$ApiFolder,
    [Parameter(Mandatory)][string]$Route,
    [Parameter(Mandatory)][string]$Body,
    [string]$IdProperty = 'id',
    [string]$Codegen = (Join-Path $PSScriptRoot '..\..\CodeGenNew.Cli\bin\Debug\net10.0\codegen.exe'),
    [int]$Port = 5194
)
Import-Module (Join-Path $PSScriptRoot 'UiAutomation.psm1') -Force -WarningAction Ignore   # only for Assert-That
$out = Join-Path ([IO.Path]::GetTempPath()) ('codegen_sqlite_' + [Guid]::NewGuid().ToString('N'))
try {
    & $Codegen generate --provider Sqlite -d $Database --project $Project -o $out --essentials --build
    Assert-That ($LASTEXITCODE -eq 0) 'codegen generates the project and it builds'
    $api = Join-Path $out $ApiFolder
    $scratch = Join-Path $api 'scratch.db'
    Copy-Item $Database $scratch
    & (Join-Path $PSScriptRoot 'Test-ApiCrud.ps1') -ApiProject $api -Route $Route -ConnectionString "Data Source=$scratch" -Body $Body -IdProperty $IdProperty -Port $Port
    if ($LASTEXITCODE -ne 0) { exit 1 }
}
finally {
    if (Test-Path $out) { Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue }
}
