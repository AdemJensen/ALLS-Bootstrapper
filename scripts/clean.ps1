[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$cleanTargets = @(
    (Join-Path $repoRoot "artifacts"),
    (Join-Path $repoRoot "src\Alls.Bootstrapper\bin"),
    (Join-Path $repoRoot "src\Alls.Bootstrapper\obj"),
    (Join-Path $repoRoot "src\Alls.Configurator\bin"),
    (Join-Path $repoRoot "src\Alls.Configurator\obj")
)
$allowedTargets = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase
)
foreach ($target in $cleanTargets) {
    [void]$allowedTargets.Add([System.IO.Path]::GetFullPath($target))
}

foreach ($target in $cleanTargets) {
    $fullPath = [System.IO.Path]::GetFullPath($target)
    if (-not $allowedTargets.Contains($fullPath)) {
        throw "Refusing to clean unexpected path: $fullPath"
    }
    if (Test-Path $fullPath) {
        Remove-Item $fullPath -Recurse -Force
    }
}

Write-Host "Removed build and packaging outputs."
