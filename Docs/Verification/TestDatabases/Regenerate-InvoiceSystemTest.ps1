# Rebuilds InvoiceSystem_test from the real InvoiceSystem: schema and data, the routines that write, the fixture rows (InvoiceSystemTest.seed.sql).
# -SeedOnly runs only the fixture rows again. -Server is the SQL Server instance (Windows authentication).
param([string]$Server = 'localhost', [switch]$SeedOnly)
& (Join-Path $PSScriptRoot 'Rebuild-TestDatabase.ps1') -Source InvoiceSystem -Project InvoiceSystemTest -Server $Server -SeedOnly:$SeedOnly
