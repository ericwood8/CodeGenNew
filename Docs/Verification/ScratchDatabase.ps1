<#
.SYNOPSIS  Makes or drops a throwaway copy of a database, so a click-through can create, edit and delete rows without touching the real one.
.DESCRIPTION
  SqlServer  : BACKUP the source database and RESTORE it as <Source>_scratch (Windows authentication, the instance in -Server).
  PostgreSql : CREATE DATABASE <Source>_scratch TEMPLATE <Source> (nobody may be connected to the source).
  MySql      : creates <source>_scratch and copies every table with CREATE TABLE ... LIKE + INSERT ... SELECT.
  Passwords are never parameters: PostgreSql reads PGPASSWORD, MySql reads MYSQL_PWD from the environment. Set them in your own shell first.
.EXAMPLE
  .\ScratchDatabase.ps1 -Provider PostgreSql -Source InvoiceSystem -User dev_login
  .\ScratchDatabase.ps1 -Provider PostgreSql -Source InvoiceSystem -User dev_login -Drop
#>
param(
    [Parameter(Mandatory)][ValidateSet('SqlServer', 'PostgreSql', 'MySql')][string]$Provider,
    [Parameter(Mandatory)][string]$Source,
    [string]$Server = 'localhost',
    [string]$User,
    [switch]$Drop,
    [string]$PsqlPath = 'C:\Program Files\PostgreSQL\18\bin\psql.exe',
    [string]$MySqlPath = 'C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe'
)
$ErrorActionPreference = 'Stop'
$scratch = "${Source}_scratch"

switch ($Provider) {
    'SqlServer' {
        # the server's own service writes the backup, so it goes to the instance's backup folder (a user's temp folder is not writable by it)
        $backupDir = (sqlcmd -S $Server -E -h -1 -W -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(400))" | Where-Object { $_ } | Select-Object -First 1).Trim()
        $backup = Join-Path $backupDir "$Source.scratch.bak"
        if ($Drop) {
            sqlcmd -S $Server -E -b -Q "IF DB_ID('$scratch') IS NOT NULL BEGIN ALTER DATABASE [$scratch] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$scratch]; END"
            Remove-Item $backup -ErrorAction SilentlyContinue
            break
        }
        sqlcmd -S $Server -E -b -Q "BACKUP DATABASE [$Source] TO DISK = N'$backup' WITH INIT, COPY_ONLY"
        if ($LASTEXITCODE) { throw "The backup of $Source failed." }
        $files = sqlcmd -S $Server -E -h -1 -W -s '|' -Q "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK = N'$backup'" | Where-Object { $_ }
        $dir = (sqlcmd -S $Server -E -h -1 -W -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(400))" | Where-Object { $_ } | Select-Object -First 1).Trim()
        $moves = foreach ($line in $files) {
            $parts = $line -split '\|'
            $extension = if ($parts[2] -eq 'L') { 'ldf' } else { 'mdf' }
            "MOVE N'$($parts[0])' TO N'$dir${scratch}_$($parts[0]).$extension'"
        }
        sqlcmd -S $Server -E -b -Q "RESTORE DATABASE [$scratch] FROM DISK = N'$backup' WITH $($moves -join ', '), REPLACE"
        if ($LASTEXITCODE) { throw "The restore of $scratch failed." }
    }
    'PostgreSql' {
        if (-not $env:PGPASSWORD) { throw 'Set PGPASSWORD in your shell first (it is never a parameter).' }
        $psql = { param($db, $sql) & $PsqlPath -h $Server -U $User -d $db -v ON_ERROR_STOP=1 -q -c $sql }
        & $psql 'postgres' "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname IN ('$scratch', '$Source') AND pid <> pg_backend_pid()" | Out-Null
        & $psql 'postgres' "DROP DATABASE IF EXISTS `"$scratch`""
        if (-not $Drop) { & $psql 'postgres' "CREATE DATABASE `"$scratch`" TEMPLATE `"$Source`"" }
    }
    'MySql' {
        if (-not $env:MYSQL_PWD) { throw 'Set MYSQL_PWD in your shell first (it is never a parameter).' }
        $my = { param($sql) & $MySqlPath -h 127.0.0.1 -u $User -N -B -e $sql }
        & $my "DROP DATABASE IF EXISTS ``$scratch``"
        if (-not $Drop) {
            & $my "CREATE DATABASE ``$scratch``"
            $tables = & $my "SELECT table_name FROM information_schema.tables WHERE table_schema = '$Source' AND table_type = 'BASE TABLE'"
            foreach ($t in $tables) { & $my "SET FOREIGN_KEY_CHECKS = 0; CREATE TABLE ``$scratch``.``$t`` LIKE ``$Source``.``$t``; INSERT INTO ``$scratch``.``$t`` SELECT * FROM ``$Source``.``$t``" }
        }
    }
}
Write-Host "$(if ($Drop) { 'Dropped' } else { 'Ready' }): $scratch on $Provider"
