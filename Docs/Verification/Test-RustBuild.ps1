<#
.SYNOPSIS  Generates the Rust API of a database, compiles it with cargo, and walks one route through create, read, update, list and delete.
.DESCRIPTION
  The unit tests only compare text; this proves the generated crate compiles and answers. It reads the database you name (never writes to it), generates the project into a temporary folder with
  codegen's `generate --essentials`, runs `cargo build` and then Test-ApiCrud.ps1 against the binary. The project file (CODEGENNEW_HOME\Projects\<Project>.config) must say Stacks=Rust and
  DatabaseProvider; OutputRust (or <ProjectName>.Rust) is the crate folder. Run it from the repository root after `dotnet build CodeGenNew.Cli`.
  Needs a Rust toolchain (rustup) and, on Windows, the Visual Studio C++ build tools. A Visual Studio install that has no complete library folder makes the linker fail with LNK1104
  (msvcrt.lib): the script looks for an installation that has the libraries and runs cargo in its developer environment (-VcVars names one).
  The database login is never a parameter: PostgreSQL reads PGHOST, PGPORT, PGUSER, PGDATABASE and PGPASSWORD, MySQL reads MYSQL_HOST, MYSQL_TCP_PORT, MYSQL_USER, MYSQL_DATABASE and MYSQL_PWD,
  SQLite reads DATABASE_FILE (set to a scratch copy by this script). Run the calls against a copy made by ScratchDatabase.ps1, never the real database.
.EXAMPLE
  $env:PGUSER = 'dev_login'; $env:PGPASSWORD = '...'; $env:PGDATABASE = 'InvoiceSystem_scratch'
  .\Docs\Verification\Test-RustBuild.ps1 -Provider PostgreSql -Database InvoiceSystem -Server localhost:5432 -User dev_login -Project InvoiceSystemRust -CrateFolder InvoiceSystem.Rust `
      -Route /api/customers -Body '{"customerId":0,"accountNumber":"V-1","customerName":"Verify","customerStatusId":1,"isTaxable":false,"dateAdded":"2026-10-04","requireCustomerPO":false}' -IdProperty customerId
#>
param(
    [Parameter(Mandatory)][ValidateSet('PostgreSql', 'MySql', 'Sqlite')][string]$Provider,
    [Parameter(Mandatory)][string]$Database,
    [string]$Server,
    [string]$User,
    [Parameter(Mandatory)][string]$Project,
    [Parameter(Mandatory)][string]$CrateFolder,
    [Parameter(Mandatory)][string]$Route,
    [Parameter(Mandatory)][string]$Body,
    [string]$IdProperty = 'id',
    [string]$UpdateProperty,
    [string]$VcVars,
    [string]$Codegen = (Join-Path $PSScriptRoot '..\..\CodeGenNew.Cli\bin\Debug\net10.0\codegen.exe'),
    [int]$Port = 5195
)
Import-Module (Join-Path $PSScriptRoot 'UiAutomation.psm1') -Force -WarningAction Ignore   # only for Assert-That
$out = Join-Path ([IO.Path]::GetTempPath()) ('codegen_rust_' + [Guid]::NewGuid().ToString('N'))

# The developer environment that has the C++ libraries: the first Visual Studio installation with msvcrt.lib.
if (-not $VcVars -and $IsWindows -ne $false) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        foreach ($root in (& $vswhere -all -products * -property installationPath)) {
            if (Get-ChildItem (Join-Path $root 'VC\Tools\MSVC') -Recurse -Filter msvcrt.lib -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\lib\\x64\\msvcrt.lib$' }) {
                $VcVars = Join-Path $root 'VC\Auxiliary\Build\vcvars64.bat'
                break
            }
        }
    }
}
# rustup puts cargo in %USERPROFILE%\.cargo\bin, which a shell opened before the install does not have
if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) { $env:PATH = (Join-Path $HOME '.cargo\bin') + [IO.Path]::PathSeparator + $env:PATH }
function Invoke-Cargo([string]$Arguments, [string]$Folder) {
    $prefix = if ($VcVars) { "call `"$VcVars`" >nul 2>&1 && " } else { '' }
    Push-Location $Folder
    try { cmd /c "$prefix cargo $Arguments 2>&1" } finally { Pop-Location }
}

try {
    $connect = if ($Provider -eq 'Sqlite') { @('--provider', 'Sqlite', '-d', $Database) } else { @('--provider', $Provider, '-S', $Server, '-d', $Database, '-U', $User, '-P', $(if ($Provider -eq 'PostgreSql') { $env:PGPASSWORD } else { $env:MYSQL_PWD })) }
    & $Codegen generate @connect --project $Project -o $out --essentials
    Assert-That ($LASTEXITCODE -eq 0) 'codegen generates the crate'
    $crate = Join-Path $out $CrateFolder

    $build = Invoke-Cargo 'build' $crate
    $built = $LASTEXITCODE -eq 0
    if (-not $built) { $build | Select-Object -Last 25 }
    Assert-That $built 'cargo build compiles the generated crate'
    if (-not $built) { exit 1 }

    $exe = Get-ChildItem (Join-Path $crate 'target\debug') -Filter *.exe | Select-Object -First 1
    if ($Provider -eq 'Sqlite') {
        $scratch = Join-Path $crate 'scratch.db'
        Copy-Item $Database $scratch
        $env:DATABASE_FILE = $scratch
    }
    $arguments = @{ Executable = $exe.FullName; Route = $Route; Body = $Body; IdProperty = $IdProperty; Port = $Port }
    if ($UpdateProperty) { $arguments.UpdateProperty = $UpdateProperty }
    & (Join-Path $PSScriptRoot 'Test-ApiCrud.ps1') @arguments
    if ($LASTEXITCODE -ne 0) { exit 1 }
}
finally {
    if (Test-Path $out) { Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue }
}
