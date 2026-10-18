# Rebuilds TimeEntry_test from the real TimeEntry: schema and data, the routines that write, the fixture rows (TimeEntryTest.seed.sql).
# -SeedOnly runs only the fixture rows again. -Server is the SQL Server instance (Windows authentication).
param([string]$Server = 'localhost', [switch]$SeedOnly)
& (Join-Path $PSScriptRoot 'Rebuild-TestDatabase.ps1') -Source TimeEntry -Project TimeEntryTest -Server $Server -SeedOnly:$SeedOnly
