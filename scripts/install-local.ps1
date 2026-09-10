[CmdletBinding()]
param(
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifact = Join-Path $repositoryRoot 'artifacts\plugin'
$manifestPath = Join-Path $repositoryRoot 'src\Flow.Launcher.Plugin.GitHubCli\plugin.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$pluginVersion = [string]$manifest.Version
if ($pluginVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "plugin.json contains an invalid stable version: $pluginVersion"
}

$pluginsRoot = [IO.Path]::GetFullPath((Join-Path $env:APPDATA 'FlowLauncher\Plugins'))
$target = [IO.Path]::GetFullPath((Join-Path $pluginsRoot "GitHubCli-$pluginVersion"))
$pluginsPrefix = $pluginsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

if (-not $target.StartsWith($pluginsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Plugin target escaped Flow Launcher's plugin directory: $target"
}

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1')
}

if (-not (Test-Path -LiteralPath (Join-Path $artifact 'plugin.json'))) {
    throw 'Build artifact is missing. Run scripts/build.ps1 first.'
}

New-Item -ItemType Directory -Path $pluginsRoot -Force | Out-Null
if (Test-Path -LiteralPath $target) {
    Remove-Item -LiteralPath $target -Recurse -Force
}
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -Path (Join-Path $artifact '*') -Destination $target -Recurse -Force

Write-Host "Installed GitHub CLI plugin to $target"
Write-Host 'Run "Restart Flow Launcher" in Flow Launcher to load it.'
