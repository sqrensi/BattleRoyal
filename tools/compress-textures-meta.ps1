# Patches TextureImporter .meta files for WebGL/Standalone compression without Unity batchmode.
$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $ProjectRoot

function Should-SkipTexture([string]$RelativePath) {
    $p = $RelativePath.Replace("\", "/")
    return $p -match "TextMesh Pro/Examples" -or
           ($p -match "PluginYourGames/Modules" -and $p -match "/Editor/Icons/")
}

function Get-MaxTextureSize([string]$RelativePath) {
    $p = $RelativePath.Replace("\", "/")
    if ($p -match "/Resources/MaterialsForSkins/" -or
        ($p -match "/Resources/Skins/" -and $p -match "/picture\.png$") -or
        $p -match "/Pictures/" -or
        $p -match "/UI/" -or
        ($p -match "Inventory" -and $p -match "\.png$")) {
        return 512
    }
    return 1024
}

function Update-PlatformBlock([string[]]$lines, [string]$buildTarget, [int]$maxSize) {
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -eq "    buildTarget: $buildTarget") {
            for ($j = $i + 1; $j -lt [Math]::Min($i + 15, $lines.Length); $j++) {
                if ($lines[$j] -match "^    maxTextureSize:") { $lines[$j] = "    maxTextureSize: $maxSize"; continue }
                if ($lines[$j] -match "^    textureFormat:") { $lines[$j] = "    textureFormat: 12"; continue }
                if ($lines[$j] -match "^    textureCompression:") { $lines[$j] = "    textureCompression: 1"; continue }
                if ($lines[$j] -match "^    crunchedCompression:") { $lines[$j] = "    crunchedCompression: 1"; continue }
                if ($lines[$j] -match "^    compressionQuality:") { $lines[$j] = "    compressionQuality: 50"; continue }
                if ($lines[$j] -match "^    overridden:") { $lines[$j] = "    overridden: 1"; break }
            }
        }
    }
    return $lines
}

$extensions = @("*.png.meta", "*.jpg.meta", "*.jpeg.meta", "*.tga.meta", "*.psd.meta", "*.tif.meta", "*.tiff.meta", "*.bmp.meta", "*.gif.meta", "*.exr.meta", "*.hdr.meta")
$changed = 0
$skipped = 0

foreach ($pattern in $extensions) {
    Get-ChildItem "Assets" -Recurse -Filter $pattern -File | ForEach-Object {
        $rel = $_.FullName.Substring($ProjectRoot.Length + 1)
        if (Should-SkipTexture $rel) {
            $skipped++
            return
        }

        $maxSize = Get-MaxTextureSize $rel
        $lines = [System.Collections.Generic.List[string]]@((Get-Content $_.FullName))
        $updated = $false

        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match "^  maxTextureSize:") {
                $newLine = "  maxTextureSize: $maxSize"
                if ($lines[$i] -ne $newLine) {
                    $lines[$i] = $newLine
                    $updated = $true
                }
                break
            }
        }

        if ($rel.Replace("\", "/") -match "Resources/MaterialsForSkins/") {
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -eq "    enableMipMap: 1") {
                    $lines[$i] = "    enableMipMap: 0"
                    $updated = $true
                }
            }
        }

        $arr = $lines.ToArray()
        $arr = Update-PlatformBlock $arr "WebGL" $maxSize
        $arr = Update-PlatformBlock $arr "Standalone" $maxSize
        $newContent = ($arr -join "`n") + "`n"
        $oldContent = (Get-Content $_.FullName -Raw)

        if ($newContent -ne $oldContent) {
            Set-Content -LiteralPath $_.FullName -Value $newContent -NoNewline
            $changed++
            $updated = $true
        }
    }
}

Write-Host "Updated texture meta files: $changed (skipped: $skipped)"
Write-Host "Return to Unity and allow reimport when prompted."
