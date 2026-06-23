$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$guidToPath = @{}
Get-ChildItem -Path (Join-Path $root "Assets") -Recurse -Filter "*.meta" | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    if ($content -match 'guid: ([a-f0-9]{32})') {
        $guidToPath[$Matches[1]] = $_.FullName.Substring(0, $_.FullName.Length - 5)
    }
}

function Get-GuidsFromText([string]$text) {
    $set = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($m in [regex]::Matches($text, 'guid: ([a-f0-9]{32})')) {
        $g = $m.Groups[1].Value
        if ($g -ne '0000000000000000d000000000000000' -and
            $g -ne '0000000000000000e000000000000000' -and
            $g -ne '0000000000000000f000000000000000') {
            [void]$set.Add($g)
        }
    }
    return $set
}

function Expand-Dependencies([System.Collections.Generic.HashSet[string]]$seedGuids) {
    $all = New-Object 'System.Collections.Generic.HashSet[string]'
    $queue = New-Object System.Collections.Queue
    foreach ($g in $seedGuids) {
        [void]$all.Add($g)
        $queue.Enqueue($g)
    }

    while ($queue.Count -gt 0) {
        $g = $queue.Dequeue()
        if (-not $guidToPath.ContainsKey($g)) { continue }
        $path = $guidToPath[$g]
        if (-not (Test-Path $path)) { continue }
        if ((Get-Item $path).PSIsContainer) { continue }

        $ext = [IO.Path]::GetExtension($path).ToLowerInvariant()
        if ($ext -notin @('.prefab', '.unity', '.mat', '.controller', '.asset', '.fbx', '.shader', '.png', '.jpg', '.jpeg', '.tga', '.wav', '.mp3', '.ogg', '.anim', '.mask', '.fontsettings', '.ttf', '.otf', '.cs', '.renderTexture', '.spriteatlas', '.guiskin')) {
            continue
        }

        $text = Get-Content $path -Raw -ErrorAction SilentlyContinue
        if ([string]::IsNullOrWhiteSpace($text)) { continue }

        foreach ($dep in Get-GuidsFromText $text) {
            if ($all.Add($dep)) {
                $queue.Enqueue($dep)
            }
        }
    }

    return $all
}

function Sum-Guids([System.Collections.Generic.HashSet[string]]$guids) {
    $total = [long]0
    $count = 0
    foreach ($g in $guids) {
        if (-not $guidToPath.ContainsKey($g)) { continue }
        $p = $guidToPath[$g]
        if (-not (Test-Path $p)) { continue }
        $item = Get-Item $p
        if ($item.PSIsContainer) { continue }
        $total += $item.Length
        $count++
    }
    return @{ MB = [math]::Round($total / 1MB, 2); Files = $count; Bytes = $total }
}

function Analyze-Scene([string]$relPath) {
    $text = Get-Content (Join-Path $root $relPath) -Raw
    $seed = Get-GuidsFromText $text
    $deps = Expand-Dependencies $seed
    $sum = Sum-Guids $deps
    return @{ Scene = $relPath; SeedGuids = $seed.Count; Deps = $deps; Sum = $sum }
}

function Get-TopAssets([System.Collections.Generic.HashSet[string]]$guids, [int]$top = 25) {
    $rows = @()
    foreach ($g in $guids) {
        if (-not $guidToPath.ContainsKey($g)) { continue }
        $p = $guidToPath[$g]
        if (-not (Test-Path $p)) { continue }
        $item = Get-Item $p
        if ($item.PSIsContainer) { continue }
        $rows += [pscustomobject]@{
            MB = [math]::Round($item.Length / 1MB, 2)
            Ext = $item.Extension.ToLowerInvariant()
            Path = $p.Substring($root.Length + 1).Replace('\', '/')
        }
    }
    return $rows | Sort-Object MB -Descending | Select-Object -First $top
}

function Get-FolderBreakdown([System.Collections.Generic.HashSet[string]]$guids) {
    $folders = @{}
    foreach ($g in $guids) {
        if (-not $guidToPath.ContainsKey($g)) { continue }
        $p = $guidToPath[$g]
        if (-not (Test-Path $p)) { continue }
        $item = Get-Item $p
        if ($item.PSIsContainer) { continue }
        $rel = $p.Substring((Join-Path $root 'Assets').Length + 1).Replace('\', '/')
        $parts = $rel.Split('/')
        $folder = if ($parts.Length -gt 1) { $parts[0] + '/' + $parts[1] } else { $parts[0] }
        if (-not $folders.ContainsKey($folder)) { $folders[$folder] = [long]0 }
        $folders[$folder] += $item.Length
    }
    return $folders.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object {
        [pscustomobject]@{ Folder = $_.Key; MB = [math]::Round($_.Value / 1MB, 2) }
    }
}

function Get-ExtBreakdown([System.Collections.Generic.HashSet[string]]$guids) {
    $exts = @{}
    foreach ($g in $guids) {
        if (-not $guidToPath.ContainsKey($g)) { continue }
        $p = $guidToPath[$g]
        if (-not (Test-Path $p)) { continue }
        $item = Get-Item $p
        if ($item.PSIsContainer) { continue }
        $ext = if ($item.Extension) { $item.Extension.ToLowerInvariant() } else { '(none)' }
        if (-not $exts.ContainsKey($ext)) { $exts[$ext] = [long]0 }
        $exts[$ext] += $item.Length
    }
    return $exts.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object {
        [pscustomobject]@{ Ext = $_.Key; MB = [math]::Round($_.Value / 1MB, 2) }
    }
}

$menu = Analyze-Scene "Assets/Scenes/MainMenu.unity"
$game = Analyze-Scene "Assets/Scenes/Game.unity"

$onlyMenu = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($g in $menu.Deps) {
    if (-not $game.Deps.Contains($g)) {
        [void]$onlyMenu.Add($g)
    }
}

$sOnlyMenu = Sum-Guids $onlyMenu
$sceneKb = (Get-Item (Join-Path $root "Assets/Scenes/MainMenu.unity")).Length / 1KB
$prefabCount = ([regex]::Matches((Get-Content (Join-Path $root "Assets/Scenes/MainMenu.unity") -Raw), 'm_SourcePrefab:')).Count

Write-Host "MainMenu scene YAML: $([math]::Round($sceneKb, 1)) KB"
Write-Host "Prefab instances in scene: $prefabCount"
Write-Host "Total dependency closure: $($menu.Sum.MB) MB ($($menu.Sum.Files) assets)"
Write-Host "MainMenu-only vs Game: $($sOnlyMenu.MB) MB ($($sOnlyMenu.Files) assets)"
Write-Host ""

Write-Host "=== By top-level folder ==="
Get-FolderBreakdown $menu.Deps | Select-Object -First 15 | Format-Table -AutoSize

Write-Host "=== By file type ==="
Get-ExtBreakdown $menu.Deps | Format-Table -AutoSize

Write-Host "=== Top 25 heaviest assets ==="
Get-TopAssets $menu.Deps 25 | Format-Table -AutoSize

Write-Host "=== Top MainMenu-only assets (not in Game) ==="
Get-TopAssets $onlyMenu 20 | Format-Table -AutoSize
