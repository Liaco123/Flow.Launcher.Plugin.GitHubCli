[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$sdkVersion = '9.0.318'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$toolsDirectory = Join-Path $repositoryRoot '.tools'
$sdkDirectory = Join-Path $toolsDirectory 'dotnet'
$dotnet = Join-Path $sdkDirectory 'dotnet.exe'
$installer = Join-Path $toolsDirectory 'dotnet-install.ps1'

if (Test-Path -LiteralPath $dotnet) {
    $installedVersion = (& $dotnet --version).Trim()
    if ($installedVersion -eq $sdkVersion) {
        Write-Host "Project-local .NET SDK $sdkVersion is ready."
        exit 0
    }
}

New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer

$signature = Get-AuthenticodeSignature -LiteralPath $installer
if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
    $signature.SignerCertificate.Subject -notmatch 'Microsoft') {
    throw 'The downloaded dotnet-install.ps1 does not have a valid Microsoft signature.'
}

& $installer -Version $sdkVersion -InstallDir $sdkDirectory -NoPath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $dotnet)) {
    throw "Failed to install .NET SDK $sdkVersion into $sdkDirectory."
}

Write-Host "Installed project-local .NET SDK $sdkVersion."
