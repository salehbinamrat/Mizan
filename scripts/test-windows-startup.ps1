param([string]$OutputRoot = "artifacts")
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifacts = Join-Path $root $OutputRoot

function Assert-Startup([string] $executable, [string] $label) {
    if (!(Test-Path -LiteralPath $executable)) { throw "Missing executable: $executable" }
    $marker = Join-Path $env:TEMP ("mizan-startup-" + [Guid]::NewGuid().ToString('N') + ".txt")
    $env:MIZAN_STARTUP_PROBE_FILE = $marker
    try {
        $proc = Start-Process -FilePath $executable -ArgumentList '--startup-smoke' -PassThru
        if (!$proc.WaitForExit(45000)) {
            if (Test-Path -LiteralPath $marker) {
                Write-Output ("Startup smoke diagnostic: " + (Get-Content -Raw -LiteralPath $marker))
            }
            else {
                Write-Output "Startup smoke never reached App.OnStartup for $label"
            }
            $log = Join-Path $env:LOCALAPPDATA 'MizanMedical\logs\startup.log'
            if (Test-Path -LiteralPath $log) {
                Get-Content -LiteralPath $log -Tail 20 | Write-Output
            }
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            throw "Windows application startup hung: $label"
        }
        $proc.Refresh()
        if ($proc.ExitCode -ne 0) {
            throw "Windows application failed startup smoke: $label ExitCode=$($proc.ExitCode)"
        }
        if (!(Test-Path -LiteralPath $marker) -or
            !((Get-Content -Raw -LiteralPath $marker).Contains('PASS:'))) {
            throw "Windows application did not initialize its login window: $label"
        }
        Write-Output "PASS Windows startup: $label"
    }
    finally {
        Remove-Item Env:\MIZAN_STARTUP_PROBE_FILE -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue
    }
}

foreach ($architecture in @('win-x86', 'win-x64')) {
    $zip = Join-Path $artifacts "MizanMedical-$architecture-Portable.zip"
    $installer = Join-Path $artifacts "installer\$architecture\MizanMedical-$architecture-Installer.exe"
    if (!(Test-Path -LiteralPath $zip) -or !(Test-Path -LiteralPath $installer)) {
        throw "Missing Portable zip or Installer for $architecture"
    }

    $tempRoot = Join-Path $env:TEMP ("mizan-qa-" + $architecture + "-" + [Guid]::NewGuid().ToString('N'))
    $portable = Join-Path $tempRoot 'portable'
    $installed = Join-Path $tempRoot 'installed'
    New-Item -ItemType Directory -Path $portable, $installed -Force | Out-Null
    try {
        Expand-Archive -LiteralPath $zip -DestinationPath $portable
        if (!(Test-Path (Join-Path $portable 'mizan-client.json'))) {
            throw "Client startup configuration is missing in Portable $architecture"
        }
        Assert-Startup (Join-Path $portable 'MizanMedical.Desktop.exe') "$architecture Portable"

        $argumentLine = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR=`"$installed`""
        $proc = Start-Process -FilePath $installer -ArgumentList $argumentLine -PassThru -Wait
        if ($proc.ExitCode -ne 0) {
            throw "Installer setup returned $($proc.ExitCode) for $architecture"
        }
        Assert-Startup (Join-Path $installed 'MizanMedical.Desktop.exe') "$architecture Installer"
    }
    finally {
        if (Test-Path $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
Write-Output 'PASS: all four final Windows distributions initialize the WPF login window.'
