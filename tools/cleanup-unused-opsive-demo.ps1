# Removes Opsive demo/shared assets not referenced by game build (not animation clips).
$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $ProjectRoot

$removePaths = @(
    "Assets/Opsive/OmniAnimation/Packs/CoreLocomotion/Demo.unity",
    "Assets/Opsive/OmniAnimation/Packs/CoreLocomotion/Documentation.pdf",
    "Assets/Opsive/OmniAnimation/Packs/CoreLocomotion/CoreLocomotionPackInfo.asset",
    "Assets/Opsive/OmniAnimation/Packs/Shared"
)

$deleted = 0
foreach ($relative in $removePaths) {
    $full = Join-Path $ProjectRoot $relative
    if (-not (Test-Path $full)) {
        continue
    }

    Remove-Item -LiteralPath $full -Force -Recurse
    $meta = "$full.meta"
    if (Test-Path $meta) {
        Remove-Item -LiteralPath $meta -Force
    }

    Write-Host "Removed $relative"
    $deleted++
}

Write-Host "Removed $deleted Opsive demo path(s). Animation FBX used by player controllers were kept."
