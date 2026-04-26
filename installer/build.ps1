# PC Audio SIP Bridge - MSI Installer Build Script (WiX v3)
# Usage: powershell -ExecutionPolicy Bypass -File build.ps1 [-Version 1.0.0]
param([string]$Version = "1.0.0")

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$WixBin    = "C:\Program Files (x86)\WiX Toolset v3.14\bin"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SrcDir    = Join-Path $ScriptDir "..\client\LoopcastUA\bin\Release"
$OutDir    = Join-Path $ScriptDir "bin\Release"

if (-not (Test-Path "$WixBin\candle.exe")) {
    Write-Error "WiX Toolset v3 not found at: $WixBin`nInstall from: https://github.com/wixtoolset/wix3/releases (wix314.exe)"
    exit 1
}
if (-not (Test-Path "$SrcDir\LoopcastUA.exe")) {
    Write-Error "Release build not found: $SrcDir - run MSBuild first."
    exit 1
}

$null = New-Item -ItemType Directory -Force $OutDir
Write-Host "=== LoopcastUA Installer v$Version ===" -ForegroundColor Cyan

# Step 1: Harvest binaries
Write-Host "[1/3] Harvesting files..."
& "$WixBin\heat.exe" dir $SrcDir -nologo -sfrag -srd -gg -gl `
    -scom -sreg `
    -cg AppFiles -dr INSTALLFOLDER -var "var.SourceDir" `
    -out "$OutDir\AppFiles.wxs"
if ($LASTEXITCODE -ne 0) { Write-Error "heat failed"; exit 1 }

# Step 2: Compile
Write-Host "[2/3] Compiling..."
& "$WixBin\candle.exe" -nologo -arch x64 `
    "-dSourceDir=$SrcDir" "-dVersion=$Version" `
    "$ScriptDir\Product.wxs" "$OutDir\AppFiles.wxs" `
    "-out" "$OutDir\"
if ($LASTEXITCODE -ne 0) { Write-Error "candle failed"; exit 1 }

# Step 3: Link
$MsiPath = "$OutDir\LoopcastUA-$Version.msi"
Write-Host "[3/3] Linking MSI..."
& "$WixBin\light.exe" -nologo `
    -ext WixUIExtension -ext WixUtilExtension `
    "$OutDir\Product.wixobj" "$OutDir\AppFiles.wixobj" `
    "-out" $MsiPath
if ($LASTEXITCODE -ne 0) { Write-Error "light failed"; exit 1 }

$SizeMB = [math]::Round((Get-Item $MsiPath).Length / 1MB, 1)
Write-Host "Done: $MsiPath ($SizeMB MB)" -ForegroundColor Green
Write-Host "Silent install: msiexec /i `"$MsiPath`" /qn"
