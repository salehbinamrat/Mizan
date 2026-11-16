# Intentionally refuses to restore into real hospital databases.
param(
  [Parameter(Mandatory=$true)][string]$BackupFile,
  [Parameter(Mandatory=$true)][string]$TargetTestDatabase,
  [switch]$ConfirmedDisposable
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
if (-not $ConfirmedDisposable -or -not $TargetTestDatabase.EndsWith('_restore_test')) {
  throw 'Restore allowed only into a disposable database ending _restore_test, with -ConfirmedDisposable.'
}
if (-not (Test-Path $BackupFile -PathType Leaf)) { throw 'Backup not found.' }
$hashFile = "$BackupFile.sha256"
if (-not (Test-Path $hashFile)) { throw 'Missing SHA256 file. Rejecting unverified backup.' }
$expected = (Get-Content $hashFile -Raw).Trim()
$actual = (Get-FileHash $BackupFile -Algorithm SHA256).Hash
if ($expected -ne $actual) { throw 'Backup checksum mismatch.' }
if (-not (Get-Command pg_restore -ErrorAction SilentlyContinue)) { throw 'pg_restore is not in PATH.' }
& pg_restore --dbname $TargetTestDatabase --no-owner --no-acl --clean --if-exists --exit-on-error $BackupFile
if ($LASTEXITCODE -ne 0) { throw 'Test restore failed.' }
Write-Host "Test restore complete: $TargetTestDatabase. Validate table counts and financial totals."
