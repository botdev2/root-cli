#Requires -Version 5.1
<#
  Compatibility wrapper. Prefer: irm https://useroot.sh/cli | iex
#>
param(
    [string]$Repo = "botdev2/root-cli",
    [string]$Tag = "latest",
    [string]$Asset = "RootCli-win-x64.zip"
)

$ErrorActionPreference = "Stop"
$env:ROOTCLI_GITHUB_REPO = $Repo
$env:ROOTCLI_TAG = $Tag
$env:ROOTCLI_ASSET = $Asset

$here = $PSScriptRoot
if ($here) {
    & (Join-Path $here "install.ps1")
} else {
    Invoke-Expression (Invoke-RestMethod -Uri "https://raw.githubusercontent.com/botdev2/root-cli/main/scripts/install.ps1")
}
