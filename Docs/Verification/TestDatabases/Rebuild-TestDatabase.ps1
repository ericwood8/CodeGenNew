<#
.SYNOPSIS  Rebuilds <Source>_test, the standing test database of a SQL Server database: its schema and data from the source, the routines that write, and fixture rows.
.DESCRIPTION
  The source database is only read. <Source>_test must be allowed to be overwritten: everything in it is replaced.
  1. backs the source up (COPY_ONLY) and restores it over <Source>_test, which is created when missing (the schema and every row of the source come with it);
  2. makes the test database's users match a limited login: the db_owner membership the source gives a user is dropped, db_datareader and db_datawriter stay,
     and EXECUTE on the dbo schema is granted (the generated routines are called by procedure);
  3. generates the routines that write (SP_Insert, SP_Update, SP_Save, SP_Delete, SP_Lookup, SP_BulkUpdate where the project names them) with the value rules of
     <Project>.config and applies them with CREATE OR ALTER;
  4. runs the seed file: rows that already break a rule, rows to create, edit and delete, a parent that is "in use", a last page of one row.
  Windows authentication; the connected Windows user needs the right to restore (dbcreator or sysadmin) and to create the routines.
  -SeedOnly runs only step 4 again, on the database as it is now.
.EXAMPLE
  .\Rebuild-TestDatabase.ps1 -Source InvoiceSystem -Project InvoiceSystemTest
  .\Rebuild-TestDatabase.ps1 -Source TimeEntry -Project TimeEntryTest -SeedOnly
#>
param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$Project,
    [string]$Server = 'localhost',
    [string]$SeedFile,
    [string]$Cli = (Join-Path $PSScriptRoot '..\..\..\CodeGenNew.Cli\bin\Debug\net10.0'),
    [switch]$SeedOnly
)
$ErrorActionPreference = 'Stop'
$test = "${Source}_test"
if (-not $SeedFile) { $SeedFile = Join-Path $PSScriptRoot "$Project.seed.sql" }

function Invoke-Sql([string]$Database, [string]$Query) {
    $out = sqlcmd -S $Server -E -d $Database -b -h -1 -W -Q "SET NOCOUNT ON; $Query"
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed in ${Database}: $out" }
    $out
}
function Invoke-SqlFile([string]$Database, [string]$Path) {
    $out = sqlcmd -S $Server -E -d $Database -b -i $Path
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed on ${Path}: $out" }
    $out
}

if (-not $SeedOnly) {
    if (-not (Test-Path $SeedFile)) { throw "No seed file $SeedFile" }
    Invoke-Sql 'master' "IF DB_ID('$Source') IS NULL THROW 50000, 'The source database $Source does not exist.', 1;" | Out-Null
    Invoke-Sql 'master' "IF DB_ID('$test') IS NULL CREATE DATABASE [$test];" | Out-Null

    Write-Host "== 1. restore $Source over $test"
    # the SQL Server service writes and reads the backup, so it goes to the instance's backup folder
    $backupDir = (Invoke-Sql 'master' "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(400))" | Where-Object { $_ } | Select-Object -First 1).Trim()
    $backup = Join-Path $backupDir "$test.rebuild.bak"
    Invoke-Sql 'master' "BACKUP DATABASE [$Source] TO DISK = N'$backup' WITH COPY_ONLY, INIT, STATS = 100;" | Out-Null

    $sourceFiles = @(Invoke-Sql 'master' "SELECT name COLLATE DATABASE_DEFAULT + '|' + type_desc COLLATE DATABASE_DEFAULT FROM sys.master_files WHERE database_id = DB_ID('$Source') ORDER BY file_id" | Where-Object { $_ })
    $testFiles = @(Invoke-Sql 'master' "SELECT physical_name COLLATE DATABASE_DEFAULT + '|' + type_desc COLLATE DATABASE_DEFAULT FROM sys.master_files WHERE database_id = DB_ID('$test') ORDER BY file_id" | Where-Object { $_ })
    $dataPath = ($testFiles | Where-Object { $_ -like '*|ROWS' } | Select-Object -First 1).Split('|')[0]
    $logPath = ($testFiles | Where-Object { $_ -like '*|LOG' } | Select-Object -First 1).Split('|')[0]
    $moves = @(); $extra = 0
    foreach ($file in $sourceFiles) {
        $logical, $kind = $file.Split('|')
        if ($kind -eq 'LOG') { $path = $logPath }
        elseif ($extra -eq 0) { $path = $dataPath; $extra++ }
        else { $path = [IO.Path]::ChangeExtension($dataPath, $null).TrimEnd('.') + "_$logical.ndf" }
        $moves += "MOVE N'$logical' TO N'$path'"
    }
    Invoke-Sql 'master' "ALTER DATABASE [$test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;" | Out-Null
    try {
        Invoke-Sql 'master' "RESTORE DATABASE [$test] FROM DISK = N'$backup' WITH REPLACE, $($moves -join ', '), STATS = 100;" | Out-Null
    }
    finally {
        Invoke-Sql 'master' "ALTER DATABASE [$test] SET MULTI_USER;" | Out-Null
    }
    Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $backup) { Write-Warning "Could not remove $backup; delete it by hand." }

    Write-Host "== 2. users of $test"
    Invoke-Sql $test @"
DECLARE @user sysname, @sql nvarchar(max);
DECLARE users CURSOR FOR SELECT name FROM sys.database_principals WHERE type IN ('S', 'U') AND name NOT IN ('dbo', 'guest', 'INFORMATION_SCHEMA', 'sys');
OPEN users; FETCH NEXT FROM users INTO @user;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'IF IS_ROLEMEMBER(''db_owner'', ' + QUOTENAME(@user, '''') + N') = 1 ALTER ROLE db_owner DROP MEMBER ' + QUOTENAME(@user) + N';'
             + N' ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@user) + N'; ALTER ROLE db_datawriter ADD MEMBER ' + QUOTENAME(@user) + N';'
             + N' GRANT EXECUTE ON SCHEMA::dbo TO ' + QUOTENAME(@user) + N';';
    EXEC sys.sp_executesql @sql;
    FETCH NEXT FROM users INTO @user;
END
CLOSE users; DEALLOCATE users;
"@ | Out-Null

    Write-Host "== 3. the routines that write"
    $exe = Join-Path $Cli 'codegen.exe'
    if (-not (Test-Path $exe)) { dotnet build (Join-Path $Cli '..\..\..') -v q | Out-Null }
    # the CLI seeds its Templates folder once; remove it so the current templates are used
    Remove-Item (Join-Path $Cli 'Templates') -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force (Join-Path $Cli 'Projects') | Out-Null
    Copy-Item (Join-Path $PSScriptRoot "$Project.config") (Join-Path $Cli 'Projects') -Force
    $config = Get-Content (Join-Path $PSScriptRoot "$Project.config") | Where-Object { $_ -match '^PlanAlso=' }
    $only = ($config -replace '^PlanAlso=', '')
    $work = Join-Path ([IO.Path]::GetTempPath()) "$test-routines"
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    & $exe generate -S $Server -d $test -E --project $Project -o $work --only $only --force
    if ($LASTEXITCODE -ne 0) { throw 'codegen generate failed' }
    $files = @(Get-ChildItem $work -Recurse -Filter *.sql)
    foreach ($file in $files) { Invoke-SqlFile $test $file.FullName | Out-Null }
    Write-Host "   applied $($files.Count) routine files"
    Remove-Item $work -Recurse -Force
}

Write-Host "== 4. the fixture rows ($([IO.Path]::GetFileName($SeedFile)))"
Invoke-SqlFile $test $SeedFile

Write-Host "== done: $test"
