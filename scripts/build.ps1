[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dotnet = Join-Path $repositoryRoot '.tools\dotnet\dotnet.exe'
$solution = Join-Path $repositoryRoot 'Flow.Launcher.Plugin.GitHubCli.sln'
$project = Join-Path $repositoryRoot 'src\Flow.Launcher.Plugin.GitHubCli\Flow.Launcher.Plugin.GitHubCli.csproj'
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$pluginOutput = Join-Path $artifactRoot 'plugin'
$zipPath = Join-Path $artifactRoot 'Flow.Launcher.Plugin.GitHubCli.zip'
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

if (-not $artifactRoot.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Artifact path escaped the repository: $artifactRoot"
}

if (-not (Test-Path -LiteralPath $dotnet)) {
    & (Join-Path $PSScriptRoot 'bootstrap.ps1')
}

$env:DOTNET_CLI_HOME = Join-Path $repositoryRoot '.tools\dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'

$lockFiles = @(
    (Join-Path $repositoryRoot 'src\Flow.Launcher.Plugin.GitHubCli\packages.lock.json'),
    (Join-Path $repositoryRoot 'tests\Flow.Launcher.Plugin.GitHubCli.Tests\packages.lock.json')
)
$restoreArguments = @(
    'restore',
    $solution,
    '--configfile',
    (Join-Path $repositoryRoot 'NuGet.config')
)
if (@($lockFiles | Where-Object { -not (Test-Path -LiteralPath $_) }).Count -eq 0) {
    $restoreArguments += '--locked-mode'
}

& $dotnet @restoreArguments
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

& $dotnet test $solution --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }

if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $pluginOutput -Force | Out-Null

& $dotnet publish $project --configuration Release --no-restore --output $pluginOutput
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

& (Join-Path $PSScriptRoot 'test-install-local.ps1')

Compress-Archive -Path (Join-Path $pluginOutput '*') -DestinationPath $zipPath -CompressionLevel Optimal
& (Join-Path $PSScriptRoot 'test-package.ps1')
Write-Host "Plugin: $pluginOutput"
Write-Host "Package: $zipPath"
