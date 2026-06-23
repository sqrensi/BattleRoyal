$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $ProjectRoot

$folders = @(
    "Skins",
    "Attachments",
    "WeaponSkins",
    "MaterialsForSkins",
    "Characters",
    "Sounds",
    "BRAudio",
    "Cases"
)

foreach ($folder in $folders) {
    $source = Join-Path "Assets/AddressableContent" $folder
    $target = Join-Path "Assets/Resources" $folder
    if (-not (Test-Path $source)) {
        Write-Host "Skip missing $source"
        continue
    }

    if (Test-Path $target) {
        $targetFiles = (Get-ChildItem -LiteralPath $target -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
        if ($targetFiles -eq 0) {
            Remove-Item -LiteralPath $target -Recurse -Force
            Write-Host "Removed empty $target"
        }
        else {
            Write-Error "Target not empty: $target ($targetFiles files)"
        }
    }

    Move-Item -LiteralPath $source -Destination $target
    Write-Host "Moved $source -> $target"
}

$contentRoot = "Assets/AddressableContent"
if (Test-Path $contentRoot) {
    $remaining = @(Get-ChildItem -LiteralPath $contentRoot -Force | Where-Object { $_.Name -ne "AddressableContent.meta" })
    if ($remaining.Count -eq 0) {
        Remove-Item -LiteralPath $contentRoot -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath "$contentRoot.meta" -Force -ErrorAction SilentlyContinue
        Write-Host "Removed empty $contentRoot"
    }
    else {
        Write-Warning "AddressableContent still has items:"
        $remaining | ForEach-Object { Write-Host "  $($_.Name)" }
    }
}

Write-Host "Done."
