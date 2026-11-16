# Server-only backup. Use PostgreSQL client tools and an ENCRYPTED destination (e.g. BitLocker).
param(
  [Parameter(Mandatory=$true)][string]$BackupDir
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
if ($env:MIZAN_BACKUP_STORAGE_ENCRYPTED -ne 'YES') {
  throw 'Only back up to verified encrypted storage. Set MIZAN_BACKUP_STORAGE_ENCRYPTED=YES after checking disk protection.'
}
if (-not (Get-Command pg_dump -ErrorAction SilentlyContinue)) { throw 'pg_dump is not installed or not in PATH.' }
if (-not (Test-Path $BackupDir -PathType Container)) { throw 'The backup directory does not exist.' }
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path (Resolve-Path $BackupDir).Path "mizan-$timestamp.dump"
try {
  & pg_dump --format=custom --no-owner --no-acl --file $backup
  if ($LASTEXITCODE -ne 0) { throw 'pg_dump failed. The partial backup will be deleted.' }
  if ((Get-Item $backup).Length -eq 0) { throw 'Backup is empty.' }
  $digest = (Get-FileHash $backup -Algorithm SHA256).Hash
  Set-Content -Path "$backup.sha256" -Value $digest -Encoding ascii
  Write-Host "Backup completed: $backup"
  Write-Host "SHA256: $digest"
  Write-Host 'IMPORTANT: Test restoration into a disposable database regularly. This script does not encrypt its own output.'
} catch {
  if (Test-Path $backup) { Remove-Item $backup -Force }
  throw
}
