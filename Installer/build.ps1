# Builds the self-contained win-x64 publish output, then compiles the Inno Setup installer.
# Run from anywhere; paths are resolved relative to this script.

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

dotnet publish "$repoRoot\Tape Player.vbproj" -p:PublishProfile=win-x64-SelfContained
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) {
    $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
}
if (-not (Test-Path $iscc)) {
    throw "ISCC.exe not found. Install Inno Setup: winget install JRSoftware.InnoSetup"
}

& $iscc "$PSScriptRoot\TapePlayer.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed" }

Write-Host "Installer built at $repoRoot\publish\installer\TapeCassettePlayerSetup.exe"
