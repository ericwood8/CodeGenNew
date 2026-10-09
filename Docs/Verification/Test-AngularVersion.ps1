<#
.SYNOPSIS  Generates the Angular front end of a project for one Angular version into a scratch folder, installs its packages, builds it and runs its tests.
.DESCRIPTION
  Proves what the AngularVersion setting promises: that package.json installs, `ng build` passes and `ng test` passes for that major version (18 to 22). It reads the database you name
  (never writes to it), copies the project file with AngularVersion set to -Version, runs `codegen generate --stack angular --essentials` into a temporary folder, then `npm install`,
  `ng build` and `ng test --watch=false` (Karma with ChromeHeadless up to Angular 20, Vitest from 21). The first failing step stops the script with exit code 1; the folder is deleted
  at the end unless -Keep is given. Needs Node and, for Angular 18 to 20, Chrome. Run it from the repository root after `dotnet build CodeGenNew.Cli`.
.EXAMPLE
  .\Docs\Verification\Test-AngularVersion.ps1 -Version 18 -Project MyProject -ConnectionArgs '-S','MYSERVER','-d','MyDatabase','-E'
#>
param(
    [Parameter(Mandatory)][ValidateRange(18, 99)][int]$Version,
    [Parameter(Mandatory)][string]$Project,
    [Parameter(Mandatory)][string[]]$ConnectionArgs,
    [string]$Codegen = (Join-Path $PSScriptRoot '..\..\CodeGenNew.Cli\bin\Debug\net10.0\codegen.exe'),
    [switch]$Keep
)
Import-Module (Join-Path $PSScriptRoot 'UiAutomation.psm1') -Force -WarningAction Ignore   # only for Assert-That
$projects = Join-Path (Split-Path $Codegen) 'Projects'
$source = Join-Path $projects "$Project.config"
$copyName = "${Project}_ng$Version"
$copy = Join-Path $projects "$copyName.config"
$out = Join-Path ([IO.Path]::GetTempPath()) ('codegen_ng_' + [Guid]::NewGuid().ToString('N'))
try {
    Assert-That (Test-Path $source) "the project file $source exists"
    $lines = Get-Content $source | Where-Object { $_ -notmatch '^\s*AngularVersion\s*=' }
    $lines + "AngularVersion=$Version" | Set-Content $copy

    & $Codegen generate @ConnectionArgs --project $copyName --stack angular -o $out --essentials
    if ($LASTEXITCODE -ne 0) { Write-Warning 'codegen reported errors or refused tables (a project that lists a table in NoApiTables does); the checks below decide.' }
    $app = Get-ChildItem $out -Recurse -Filter package.json | Where-Object { $_.FullName -notmatch 'node_modules' } | Select-Object -First 1
    Assert-That ($null -ne $app) 'a package.json was written'
    Push-Location $app.DirectoryName
    try {
        [string[]]$runner = if ($Version -ge 21) { @('--watch=false') } else { @('--watch=false', '--browsers=ChromeHeadless') }
        npm install --no-audit --no-fund
        $installed = $LASTEXITCODE -eq 0
        Assert-That $installed "npm install succeeds for Angular $Version"
        if ($installed) {
            npx ng build
            $built = $LASTEXITCODE -eq 0
            Assert-That $built "ng build succeeds for Angular $Version"
            if ($built) {
                npx ng test @runner
                Assert-That ($LASTEXITCODE -eq 0) "ng test succeeds for Angular $Version"
            }
        }
    }
    finally { Pop-Location }
}
finally {
    Remove-Item $copy -ErrorAction SilentlyContinue
    if (-not $Keep -and (Test-Path $out)) { Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue }
}
if ($script:Failed -or $LASTEXITCODE -ne 0) { exit 1 }
