[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$SkipBuild,
    [string]$PluginsRoot = (Join-Path $env:APPDATA 'FlowLauncher\Plugins')
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

$pluginsRoot = [IO.Path]::GetFullPath($PluginsRoot)
$target = [IO.Path]::GetFullPath((Join-Path $pluginsRoot "GitHubCli-$pluginVersion"))
$pluginsPrefix = $pluginsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

if (-not $target.StartsWith($pluginsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Plugin target escaped Flow Launcher's plugin directory: $target"
}

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1')
}

$artifactManifestPath = Join-Path $artifact 'plugin.json'
if (-not (Test-Path -LiteralPath $artifactManifestPath -PathType Leaf)) {
    throw 'Build artifact is missing. Run scripts/build.ps1 first.'
}

$artifactManifest = Get-Content -Raw -LiteralPath $artifactManifestPath | ConvertFrom-Json
if ($artifactManifest.ID -ne $manifest.ID -or $artifactManifest.Version -ne $pluginVersion) {
    throw 'Build artifact ID/version does not match the source manifest. Run scripts/build.ps1 first.'
}

$artifactPrefix = [IO.Path]::GetFullPath($artifact).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
foreach ($relativePath in @($artifactManifest.ExecuteFileName, $artifactManifest.IcoPath)) {
    if ([string]::IsNullOrWhiteSpace($relativePath)) {
        throw 'Build artifact manifest is missing its entry DLL or icon path.'
    }
    $requiredPath = [IO.Path]::GetFullPath((Join-Path $artifact $relativePath))
    if (-not $requiredPath.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Build artifact file is missing or outside the plugin directory: $relativePath"
    }
}

if (Test-Path -LiteralPath $pluginsRoot) {
    if ((Get-Item -LiteralPath $pluginsRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Refusing to install into a plugin directory that is a reparse point: $pluginsRoot"
    }
}

$pluginsRootWithoutSeparator = $pluginsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)
$installedVersions = @()
if (Test-Path -LiteralPath $pluginsRoot) {
    foreach ($directory in Get-ChildItem -LiteralPath $pluginsRoot -Directory) {
        if (($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to inspect a plugin directory that is a reparse point: $($directory.FullName)"
        }
        $installedManifestPath = Join-Path $directory.FullName 'plugin.json'
        if (-not (Test-Path -LiteralPath $installedManifestPath -PathType Leaf)) { continue }
        try {
            $installedManifest = Get-Content -Raw -LiteralPath $installedManifestPath | ConvertFrom-Json
        }
        catch {
            Write-Warning "Skipping unreadable plugin manifest: $installedManifestPath"
            continue
        }
        if ($installedManifest.ID -eq $manifest.ID) {
            $installedVersions += $directory
        }
    }
}

foreach ($installedVersion in $installedVersions) {
    $installedPath = [IO.Path]::GetFullPath($installedVersion.FullName)
    $installedParent = [IO.Path]::GetFullPath($installedVersion.Parent.FullName).TrimEnd([IO.Path]::DirectorySeparatorChar)

    if (-not $installedPath.StartsWith($pluginsPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not $installedParent.Equals($pluginsRootWithoutSeparator, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Installed plugin path escaped Flow Launcher's plugin directory: $installedPath"
    }

    if (($installedVersion.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to move a plugin directory that is a reparse point: $installedPath"
    }

}

if ((Test-Path -LiteralPath $target) -and $target -notin @($installedVersions | ForEach-Object { $_.FullName })) {
    throw "Installation target exists but does not belong to this plugin: $target"
}

# Backups must be outside Plugins: Flow scans every immediate subdirectory.
$dataRoot = [IO.Directory]::GetParent($pluginsRoot).FullName
$backupRoot = [IO.Path]::GetFullPath((Join-Path $dataRoot 'PluginBackups'))
if ((Test-Path -LiteralPath $backupRoot) -and
    ((Get-Item -LiteralPath $backupRoot).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw "Refusing to use a backup directory that is a reparse point: $backupRoot"
}
$backupSession = Join-Path $backupRoot ("GitHubCli-" + [guid]::NewGuid().ToString('N'))
$staging = Join-Path $backupSession 'staging'
$previous = Join-Path $backupSession 'previous'

if (-not $PSCmdlet.ShouldProcess($target, "Back up $($installedVersions.Count) existing copies and install GitHub CLI $pluginVersion")) {
    return
}

New-Item -ItemType Directory -Path $pluginsRoot, $staging, $previous -Force | Out-Null
Copy-Item -Path (Join-Path $artifact '*') -Destination $staging -Recurse -Force
$movedVersions = @()
try {
    foreach ($installedVersion in $installedVersions) {
        Move-Item -LiteralPath $installedVersion.FullName -Destination (Join-Path $previous $installedVersion.Name)
        $movedVersions += $installedVersion
    }
    Move-Item -LiteralPath $staging -Destination $target
}
catch {
    foreach ($installedVersion in $movedVersions) {
        Move-Item -LiteralPath (Join-Path $previous $installedVersion.Name) -Destination $installedVersion.FullName
    }
    throw
}

Write-Host "Installed GitHub CLI plugin to $target"
Write-Host "Previous copies backed up to $previous"
Write-Host 'Run "Restart Flow Launcher" in Flow Launcher to load it.'
