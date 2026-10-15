<#
.SYNOPSIS
    Builds the Keyservant Windows installer (MSI).

.DESCRIPTION
    Publishes the app, the CLI and (if possible) the layout installer stub into one folder and wraps it in an
    MSI with WiX. Installs WiX as a .NET tool if it isn't there yet.

    Run from the repository root:
        .\tools\build-installer.ps1                  # x64, writes artifacts\Keyservant-x64.msi
        .\tools\build-installer.ps1 -Arch arm64      # Windows on ARM
        .\tools\build-installer.ps1 -Version 0.2.0

    Needs the .NET 10 SDK. The installer stub (for "Create installer...") is NativeAOT and needs the
    "Desktop development with C++" workload of Visual Studio or the Build Tools; without it the MSI is built
    anyway and that one feature is missing.
#>
param(
    [ValidateSet('x64', 'arm64')] [string] $Arch = 'x64',
    [string] $Version = '0.1.0',
    [string] $Output = 'artifacts'
)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

$rid = "win-$Arch"
$platform = if ($Arch -eq 'arm64') { 'ARM64' } else { 'x64' }
$publish = Join-Path $Output "publish-$Arch"
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
New-Item -ItemType Directory -Force $Output | Out-Null

function Invoke-Checked([string] $what, [scriptblock] $command) {
    Write-Host "== $what"
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)." }
}

Invoke-Checked 'Publish app' { dotnet publish src/Keyservant.App/Keyservant.App.csproj -c Release "-p:Platform=$platform" -r $rid --self-contained "-p:Version=$Version" -o $publish }
Invoke-Checked 'Publish CLI' { dotnet publish src/Keyservant.Cli/Keyservant.Cli.csproj -c Release -r $rid --self-contained -p:PublishSingleFile=true "-p:Version=$Version" -o $publish }

# The layout installer stub. NativeAOT for ARM64 needs ARM64 C++ tools, so ARM64 builds use the x64 stub
# (it runs emulated and installs the right DLL for the PC).
Write-Host '== Publish installer stub'
$stubRid = if ($Arch -eq 'arm64') { 'win-x64' } else { $rid }
$stubOut = Join-Path $Output "stub-$stubRid"
dotnet publish src/Keyservant.Stub/Keyservant.Stub.csproj -c Release -r $stubRid "-p:Version=$Version" -o $stubOut
if ($LASTEXITCODE -eq 0) { Copy-Item (Join-Path $stubOut 'Keyservant.Stub.exe') $publish }
else { Write-Warning 'The installer stub could not be built (C++ build tools missing?). "Create installer..." will not be available.' }

# Debug symbols aren't needed on users' PCs.
Get-ChildItem $publish -Recurse -Filter *.pdb | Remove-Item

if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    Invoke-Checked 'Install WiX' { dotnet tool install --global wix --version 5.0.2 }
    $env:PATH += ";$env:USERPROFILE\.dotnet\tools"
}

foreach ($ext in 'WixToolset.UI.wixext', 'WixToolset.Util.wixext') {
    if (-not ((wix extension list -g) -match [regex]::Escape($ext))) {
        Invoke-Checked "Add WiX extension $ext" { wix extension add -g "$ext/5.0.2" }
    }
}

$msi = Join-Path $Output "Keyservant-$Arch.msi"
$fullPublish = (Resolve-Path $publish).Path
Invoke-Checked 'Build MSI' { wix build installer/Keyservant.wxs -arch $Arch -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext -d "PublishDir=$fullPublish" -d "Version=$Version" -o $msi }
Write-Host "Created $msi"
