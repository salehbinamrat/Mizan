param([string]$OutputRoot = "artifacts")
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$target = Join-Path $projectRoot $OutputRoot
New-Item -ItemType Directory -Path $target -Force | Out-Null

$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
if (!(Test-Path $iscc)) { throw 'Inno Setup 6 not installed. Required to build installers.' }
$brandingIcon = Join-Path $projectRoot 'src\MizanMedical.Desktop\Assets\MizanMedical.ico'
if (!(Test-Path -LiteralPath $brandingIcon)) { throw 'Missing Mizan Medical branding icon.' }
foreach ($arch in @('win-x86','win-x64')) {
    $publishDir = Join-Path $target "publish\$arch"
    $packageDir = Join-Path $target "portable\$arch"
    $installerDir = Join-Path $target "installer\$arch"
    New-Item -ItemType Directory -Path $publishDir, $packageDir, $installerDir -Force | Out-Null
    dotnet publish (Join-Path $projectRoot 'src\MizanMedical.Desktop\MizanMedical.Desktop.csproj') `
        --configuration Release --runtime $arch --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None --output $publishDir
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $arch" }
    if (!(Test-Path (Join-Path $publishDir 'MizanMedical.Desktop.exe'))) { throw 'The main executable is missing.' }
    Copy-Item (Join-Path $publishDir '*') -Destination $packageDir -Recurse -Force
    $zip = Join-Path $target "MizanMedical-$arch-Portable.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zip

    & $iscc "/DArch=$arch" "/DSourceDir=$publishDir" "/DOutputDir=$installerDir" "/DIconFile=$brandingIcon" `
        (Join-Path $projectRoot 'installer\MizanMedical.iss')
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed: $arch" }
    $setup = Join-Path $installerDir "MizanMedical-$arch-Installer.exe"
    if (!(Test-Path $setup)) { throw "Installer output missing: $arch" }
    Write-Output "READY $arch Portable: $zip"
    Write-Output "READY $arch Installer: $setup"
}
