[CmdletBinding()]
param(
    [string]$DotnetPath = "dotnet",
    [ValidateNotNullOrEmpty()]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "artifacts\ALLS-win-x64"
$archivePath = Join-Path $repoRoot "artifacts\ALLS-win-x64.zip"
$expectedPublishDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts\ALLS-win-x64"))

if ([System.IO.Path]::GetFullPath($publishDir) -ne $expectedPublishDir) {
    throw "Refusing to clean unexpected publish directory: $publishDir"
}

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
if (Test-Path $archivePath) {
    Remove-Item $archivePath -Force
}
New-Item $publishDir -ItemType Directory -Force | Out-Null

& $DotnetPath publish (Join-Path $repoRoot "src\Alls.Bootstrapper\Alls.Bootstrapper.csproj") `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -m:1 -o $publishDir
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $DotnetPath publish (Join-Path $repoRoot "src\Alls.Configurator\Alls.Configurator.csproj") `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -m:1 -o $publishDir
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Compress-Archive -Path $publishDir -DestinationPath $archivePath -Force

Write-Host "Published directory: $publishDir"
Write-Host "Published archive:   $archivePath"
