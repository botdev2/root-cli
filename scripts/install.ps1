#Requires -Version 5.1
<#
  Root CLI installer (Windows). Safe for:
    irm https://useroot.sh/cli | iex

  Downloads the latest GitHub Release zip and puts `rootcli` on PATH.
#>
$ErrorActionPreference = "Stop"

$Repo = if ($env:ROOTCLI_GITHUB_REPO) { $env:ROOTCLI_GITHUB_REPO } else { "botdev2/root-cli" }
$Tag = if ($env:ROOTCLI_TAG) { $env:ROOTCLI_TAG } else { "latest" }
$Asset = if ($env:ROOTCLI_ASSET) { $env:ROOTCLI_ASSET } else { "RootCli-win-x64.zip" }

$api = if ($Tag -eq "latest") {
    "https://api.github.com/repos/$Repo/releases/latest"
} else {
    "https://api.github.com/repos/$Repo/releases/tags/$Tag"
}

Write-Host "Root CLI — installing from release ($Tag)…"
$release = Invoke-RestMethod -Uri $api -Headers @{ "User-Agent" = "rootcli-install" }
$assetInfo = $release.assets | Where-Object { $_.name -eq $Asset } | Select-Object -First 1
if (-not $assetInfo) {
    $names = ($release.assets | ForEach-Object { $_.name }) -join ", "
    throw "Asset '$Asset' not found on release. Available: $names"
}

$destRoot = Join-Path $env:LOCALAPPDATA "root-cli\app"
$zip = Join-Path $env:TEMP $Asset
Write-Host "Downloading $($assetInfo.browser_download_url)…"
Invoke-WebRequest -Uri $assetInfo.browser_download_url -OutFile $zip -UseBasicParsing

if (Test-Path $destRoot) { Remove-Item $destRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $destRoot | Out-Null
Expand-Archive -Path $zip -DestinationPath $destRoot -Force

$exe = Get-ChildItem -Path $destRoot -Filter "RootCli.exe" -Recurse | Select-Object -First 1
if (-not $exe) { throw "RootCli.exe missing inside the zip." }
$exePath = $exe.FullName

$binDir = Join-Path $env:LOCALAPPDATA "root-cli\bin"
New-Item -ItemType Directory -Force -Path $binDir | Out-Null

$cmdShim = Join-Path $binDir "rootcli.cmd"
@"
@echo off
"$exePath" %*
"@ | Set-Content -LiteralPath $cmdShim -Encoding ASCII

$psShim = Join-Path $binDir "rootcli.ps1"
@"
& "$exePath" @args
"@ | Set-Content -LiteralPath $psShim -Encoding UTF8

$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ([string]::IsNullOrWhiteSpace($userPath)) { $userPath = "" }
$pathParts = $userPath -split ';' | Where-Object { $_ -and $_.Trim() -ne '' }
if (-not ($pathParts | Where-Object { $_.TrimEnd('\') -ieq $binDir.TrimEnd('\') })) {
    $newPath = if ($userPath.Trim()) { "$userPath;$binDir" } else { $binDir }
    [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
    $env:Path = "$env:Path;$binDir"
}

$profilePath = $PROFILE.CurrentUserAllHosts
if ([string]::IsNullOrWhiteSpace($profilePath)) {
    $profilePath = Join-Path $HOME "Documents\PowerShell\profile.ps1"
}
$profileDir = Split-Path -Parent $profilePath
if (-not (Test-Path -LiteralPath $profileDir)) {
    New-Item -ItemType Directory -Force -Path $profileDir | Out-Null
}
if (-not (Test-Path -LiteralPath $profilePath)) {
    New-Item -ItemType File -Force -Path $profilePath | Out-Null
}

$begin = "# >>> rootcli-alias BEGIN"
$end = "# >>> rootcli-alias END"
$block = @"
$begin
# Auto-maintained by Root CLI install - do not edit inside this block.
Set-Alias -Name rootcli -Value '$exePath' -Scope Global -Force -ErrorAction SilentlyContinue
$end
"@

$existing = Get-Content -LiteralPath $profilePath -Raw -ErrorAction SilentlyContinue
if ($null -eq $existing) { $existing = "" }
if ($existing -match [regex]::Escape($begin)) {
    $pattern = "(?s)" + [regex]::Escape($begin) + ".*?" + [regex]::Escape($end)
    $updated = [regex]::Replace($existing, $pattern, $block.TrimEnd())
} else {
    if ($existing.Length -gt 0 -and -not $existing.EndsWith("`n")) { $existing += "`r`n" }
    $updated = $existing + "`r`n" + $block + "`r`n"
}
Set-Content -LiteralPath $profilePath -Value $updated -Encoding UTF8
Set-Alias -Name rootcli -Value $exePath -Scope Global -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Installed Root CLI → $exePath"
Write-Host "Open a new terminal (CMD / PowerShell / VS Code), then:"
Write-Host "  rootcli here"
Write-Host "  rootcli ask `"what is this?`""
Write-Host "  rootcli login"
