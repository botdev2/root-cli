# Installs / refreshes the `rootcli` PowerShell alias (and a cmd shim) after each build.
param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $ExePath)) {
    Write-Host "rootcli alias: exe not found: $ExePath" -ForegroundColor Yellow
    exit 0
}

$exe = (Resolve-Path -LiteralPath $ExePath).Path
$binDir = Join-Path $env:LOCALAPPDATA "root-cli\bin"
New-Item -ItemType Directory -Force -Path $binDir | Out-Null

# cmd.exe shim — so `rootcli` works outside PowerShell if bin is on PATH
$cmdShim = Join-Path $binDir "rootcli.cmd"
@"
@echo off
"$exe" %*
"@ | Set-Content -LiteralPath $cmdShim -Encoding ASCII

# PowerShell shim (function-friendly)
$psShim = Join-Path $binDir "rootcli.ps1"
@"
& "$exe" @args
"@ | Set-Content -LiteralPath $psShim -Encoding UTF8

# Ensure %LOCALAPPDATA%\root-cli\bin is on the user PATH
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ([string]::IsNullOrWhiteSpace($userPath)) {
    $userPath = ""
}
$pathParts = $userPath -split ';' | Where-Object { $_ -and $_.Trim() -ne '' }
if (-not ($pathParts | Where-Object { $_.TrimEnd('\') -ieq $binDir.TrimEnd('\') })) {
    $newPath = if ($userPath.Trim()) { "$userPath;$binDir" } else { $binDir }
    [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
    $env:Path = "$env:Path;$binDir"
    Write-Host "rootcli alias: added to user PATH -> $binDir" -ForegroundColor DarkCyan
}

# Patch PowerShell profile with a replaceable marker block
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
# Auto-maintained by RootCli build - do not edit inside this block.
Set-Alias -Name rootcli -Value '$exe' -Scope Global -Force -ErrorAction SilentlyContinue
$end
"@

$existing = Get-Content -LiteralPath $profilePath -Raw -ErrorAction SilentlyContinue
if ($null -eq $existing) { $existing = "" }

if ($existing -match [regex]::Escape($begin)) {
    $pattern = "(?s)" + [regex]::Escape($begin) + ".*?" + [regex]::Escape($end)
    $updated = [regex]::Replace($existing, $pattern, $block.TrimEnd())
} else {
    if ($existing.Length -gt 0 -and -not $existing.EndsWith("`n")) {
        $existing += "`r`n"
    }
    $updated = $existing + "`r`n" + $block + "`r`n"
}

Set-Content -LiteralPath $profilePath -Value $updated -Encoding UTF8

# Apply for the current process too (so `dotnet build` shells can use it immediately)
Set-Alias -Name rootcli -Value $exe -Scope Global -Force -ErrorAction SilentlyContinue
Write-Host "rootcli alias: $exe" -ForegroundColor Cyan
Write-Host "rootcli alias: profile -> $profilePath" -ForegroundColor DarkGray
Write-Host "rootcli alias: open a new terminal (or . `$PROFILE) to load it" -ForegroundColor DarkGray
