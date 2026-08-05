#Requires -Version 5.1
<#
.SYNOPSIS
  Download RootCli from GitHub Releases and put `rootcli` on PATH (Windows).

.EXAMPLE
  irm https://raw.githubusercontent.com/botdev2/root-cli/main/scripts/install-from-github.ps1 | iex

.EXAMPLE
  .\install-from-github.ps1 -Repo botdev2/root-cli -Tag latest
#>
param(
    [string]$Repo = "botdev2/root-cli",
    [string]$Tag = "latest",
    [string]$Asset = "RootCli-win-x64.zip"
)

$ErrorActionPreference = "Stop"
$api = if ($Tag -eq "latest") {
    "https://api.github.com/repos/$Repo/releases/latest"
} else {
    "https://api.github.com/repos/$Repo/releases/tags/$Tag"
}

Write-Host "Fetching $api …"
$release = Invoke-RestMethod -Uri $api -Headers @{ "User-Agent" = "rootcli-install" }
$assetInfo = $release.assets | Where-Object { $_.name -eq $Asset } | Select-Object -First 1
if (-not $assetInfo) {
    $names = ($release.assets | ForEach-Object { $_.name }) -join ", "
    throw "Asset '$Asset' not found. Available: $names"
}

$destRoot = Join-Path $env:LOCALAPPDATA "root-cli\app"
$zip = Join-Path $env:TEMP $Asset
Write-Host "Downloading $($assetInfo.browser_download_url) …"
Invoke-WebRequest -Uri $assetInfo.browser_download_url -OutFile $zip -UseBasicParsing

if (Test-Path $destRoot) { Remove-Item $destRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $destRoot | Out-Null
Expand-Archive -Path $zip -DestinationPath $destRoot -Force

$exe = Get-ChildItem -Path $destRoot -Filter "RootCli.exe" -Recurse | Select-Object -First 1
if (-not $exe) { throw "RootCli.exe missing inside the zip." }

& (Join-Path $PSScriptRoot "install-rootcli-alias.ps1") -ExePath $exe.FullName
Write-Host ""
Write-Host "Installed. Open a new terminal, cd into a project, then:"
Write-Host "  rootcli here"
Write-Host "  rootcli ask `"what is this?`""
