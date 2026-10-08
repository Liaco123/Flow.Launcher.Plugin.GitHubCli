[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifact = Join-Path $repositoryRoot 'artifacts\plugin'
$manifest = Get-Content -Raw -LiteralPath (Join-Path $artifact 'plugin.json') | ConvertFrom-Json
$testRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot ('.tools\installer-tests-' + [guid]::NewGuid().ToString('N'))))
$toolsPrefix = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '.tools')).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $testRoot.StartsWith($toolsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Installer test directory escaped the repository tooling directory.'
}
$plugins = Join-Path $testRoot 'Plugins'
$backupRoot = Join-Path $testRoot 'PluginBackups'
$installer = Join-Path $PSScriptRoot 'install-local.ps1'
$targetName = "GitHubCli-$($manifest.Version)"

function Assert-Condition([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

function Get-MatchingDirectories {
    @(Get-ChildItem -LiteralPath $plugins -Directory | Where-Object {
        $path = Join-Path $_.FullName 'plugin.json'
        (Test-Path -LiteralPath $path) -and
        (Get-Content -Raw -LiteralPath $path | ConvertFrom-Json).ID -eq $manifest.ID
    })
}

function New-PluginCopy([string]$name) {
    $directory = Join-Path $plugins $name
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    Copy-Item -Path (Join-Path $artifact '*') -Destination $directory -Recurse -Force
}

try {
    foreach ($name in @($targetName, 'Flow.Launcher.Plugin.GitHubCli-random-one', 'Flow.Launcher.Plugin.GitHubCli-random-two')) {
        New-PluginCopy $name
    }
    # Folder names must not determine plugin identity.
    New-PluginCopy 'GitHubCli-unrelated'
    $unrelatedManifest = Join-Path $plugins 'GitHubCli-unrelated\plugin.json'
    $otherPlugin = Get-Content -Raw -LiteralPath $unrelatedManifest | ConvertFrom-Json
    $otherPlugin.ID = '00000000-0000-0000-0000-000000000001'
    $otherPlugin | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $unrelatedManifest -Encoding UTF8
    $unrelatedHash = (Get-FileHash -LiteralPath $unrelatedManifest).Hash

    & $installer -SkipBuild -PluginsRoot $plugins -WhatIf
    Assert-Condition (@(Get-MatchingDirectories).Count -eq 3) 'WhatIf changed the installed plugins.'
    Assert-Condition (-not (Test-Path -LiteralPath $backupRoot)) 'WhatIf created a backup directory.'
    Write-Host 'PASS: WhatIf leaves installed files unchanged.'

    & $installer -SkipBuild -PluginsRoot $plugins -Confirm:$false
    Assert-Condition (@(Get-MatchingDirectories).Count -eq 1) 'Duplicate plugin IDs remain after installation.'
    $backupSessions = @(Get-ChildItem -LiteralPath $backupRoot -Directory)
    Assert-Condition ($backupSessions.Count -eq 1) 'Expected one backup session.'
    $backups = @(Get-ChildItem -LiteralPath (Join-Path $backupSessions[0].FullName 'previous') -Directory)
    Assert-Condition ($backups.Count -eq 3) 'Not all duplicate copies were backed up.'
    foreach ($backup in $backups) {
        Assert-Condition (Test-Path -LiteralPath (Join-Path $backup.FullName $manifest.ExecuteFileName)) 'A backup is incomplete.'
    }
    Assert-Condition ((Get-FileHash -LiteralPath $unrelatedManifest).Hash -eq $unrelatedHash) 'An unrelated plugin was changed.'
    Write-Host 'PASS: all matching IDs are backed up; unrelated plugins are preserved.'

    & $installer -SkipBuild -PluginsRoot $plugins -Confirm:$false
    Assert-Condition (@(Get-MatchingDirectories).Count -eq 1) 'Reinstall created a duplicate.'
    Assert-Condition (@(Get-ChildItem -LiteralPath $backupRoot -Directory).Count -eq 2) 'Reinstall did not preserve its previous copy.'
    $installedFiles = @(Get-ChildItem -LiteralPath (Join-Path $plugins $targetName) -Recurse -File)
    foreach ($file in $installedFiles) {
        $relativePath = $file.FullName.Substring((Join-Path $plugins $targetName).Length + 1)
        Assert-Condition ((Get-FileHash -LiteralPath $file.FullName).Hash -eq (Get-FileHash -LiteralPath (Join-Path $artifact $relativePath)).Hash) "Installed file differs from the artifact: $relativePath"
    }
    Assert-Condition ($installedFiles.Count -eq @(Get-ChildItem -LiteralPath $artifact -Recurse -File).Count) 'Installed file count differs from the artifact.'
    Write-Host 'PASS: reinstall leaves one complete copy matching the build artifact.'

    # A colliding target directory belonging to another plugin must not be overwritten.
    $collisionRoot = Join-Path $testRoot 'collision\Plugins'
    $collisionTarget = Join-Path $collisionRoot $targetName
    New-Item -ItemType Directory -Path $collisionTarget -Force | Out-Null
    Copy-Item -LiteralPath $unrelatedManifest -Destination (Join-Path $collisionTarget 'plugin.json')
    $rejected = $false
    try { & $installer -SkipBuild -PluginsRoot $collisionRoot -Confirm:$false }
    catch { $rejected = $_.Exception.Message -like 'Installation target exists but does not belong to this plugin:*' }
    Assert-Condition $rejected 'Installer did not reject a target belonging to another plugin.'
    Assert-Condition (-not (Test-Path -LiteralPath (Join-Path $testRoot 'collision\PluginBackups'))) 'Rejected installation changed files.'
    Write-Host 'PASS: unrelated target collisions are rejected before changing files.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
