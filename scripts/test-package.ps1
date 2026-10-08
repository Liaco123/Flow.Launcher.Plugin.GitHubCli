[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifact = Join-Path $repositoryRoot 'artifacts\plugin'
$zipPath = Join-Path $repositoryRoot 'artifacts\Flow.Launcher.Plugin.GitHubCli.zip'
$sourceManifest = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'src\Flow.Launcher.Plugin.GitHubCli\plugin.json') | ConvertFrom-Json

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $manifestEntry = $archive.GetEntry('plugin.json')
    if ($null -eq $manifestEntry) {
        throw 'Package must contain plugin.json at the ZIP root.'
    }
    $reader = [IO.StreamReader]::new($manifestEntry.Open())
    try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json }
    finally { $reader.Dispose() }

    if ($manifest.ID -ne $sourceManifest.ID -or $manifest.Version -ne $sourceManifest.Version) {
        throw 'Package ID/version does not match the source manifest.'
    }
    foreach ($relativePath in @($manifest.ExecuteFileName, $manifest.IcoPath)) {
        $entryPath = $relativePath.Replace('\', '/')
        if ($null -eq $archive.GetEntry($entryPath)) {
            throw "Package is missing the manifest's required file: $entryPath"
        }
    }

    foreach ($language in @('en', 'zh-cn', 'zh-tw')) {
        if ($null -eq $archive.GetEntry("Languages/$language.xaml")) {
            throw "Package is missing Flow language resources: $language"
        }
    }

    $files = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) })
    $artifactFiles = @(Get-ChildItem -LiteralPath $artifact -Recurse -File)
    if ($files.Count -ne $artifactFiles.Count) {
        throw 'ZIP file count does not match the published plugin.'
    }
    $artifactPrefix = [IO.Path]::GetFullPath($artifact).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $seenPaths = @{}
    foreach ($entry in $files) {
        if ($entry.Name -eq 'Flow.Launcher.Plugin.dll' -or $entry.Name -match '\.(pdb|xml)$') {
            throw "Package contains a host assembly or development-only file: $($entry.FullName)"
        }
        if ($seenPaths.ContainsKey($entry.FullName)) {
            throw "Package contains a duplicate path: $($entry.FullName)"
        }
        $seenPaths[$entry.FullName] = $true
        $artifactFile = [IO.Path]::GetFullPath((Join-Path $artifact $entry.FullName))
        if (-not $artifactFile.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $artifactFile -PathType Leaf)) {
            throw "ZIP entry does not match a published file: $($entry.FullName)"
        }
        $stream = $entry.Open()
        try { $zipHash = (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash }
        finally { $stream.Dispose() }
        if ($zipHash -ne (Get-FileHash -LiteralPath $artifactFile -Algorithm SHA256).Hash) {
            throw "ZIP entry differs from its published file: $($entry.FullName)"
        }
    }
    Write-Host "PASS: installable ZIP with $($files.Count) matching files, root manifest/entry DLL/icon, no host API DLL or PDB/XML files."
}
finally { $archive.Dispose() }
