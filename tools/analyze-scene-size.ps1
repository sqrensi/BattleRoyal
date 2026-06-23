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
        if ($ext -notin @('.prefab', '.unity', '.mat', '.controller', '.asset', '.fbx', '.shader', '.png', '.jpg', '.jpeg', '.tga', '.wav', '.mp3', '.ogg', '.anim', '.mask', '.fontsettings', '.ttf', '.otf', '.cs')) {
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
    return @{ MB = [math]::Round($total / 1MB, 2); Files = $count }
}

function Analyze-Scene([string]$relPath) {
    $text = Get-Content (Join-Path $root $relPath) -Raw
    $seed = Get-GuidsFromText $text
    $deps = Expand-Dependencies $seed
    $sum = Sum-Guids $deps
    return @{ Scene = $relPath; SeedGuids = $seed.Count; Deps = $deps; Sum = $sum }
}

$one = Analyze-Scene "Assets/Scenes/1x1.unity"
$game = Analyze-Scene "Assets/Scenes/Game.unity"
$menu = Analyze-Scene "Assets/Scenes/MainMenu.unity"

$only1x1 = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($g in $one.Deps) {
    if (-not $game.Deps.Contains($g) -and -not $menu.Deps.Contains($g)) {
        [void]$only1x1.Add($g)
    }
}

$onlyGame = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($g in $game.Deps) {
    if (-not $one.Deps.Contains($g) -and -not $menu.Deps.Contains($g)) {
        [void]$onlyGame.Add($g)
    }
}

$shared1x1Game = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($g in $one.Deps) {
    if ($game.Deps.Contains($g)) {
        [void]$shared1x1Game.Add($g)
    }
}

$sOnly1x1 = Sum-Guids $only1x1
$sOnlyGame = Sum-Guids $onlyGame
$sShared = Sum-Guids $shared1x1Game

Write-Host "=== Scene dependency sizes (source assets, uncompressed) ==="
Write-Host ("1x1 total closure: {0} MB ({1} assets)" -f $one.Sum.MB, $one.Sum.Files)
Write-Host ("Game total closure: {0} MB ({1} assets)" -f $game.Sum.MB, $game.Sum.Files)
Write-Host ("MainMenu total closure: {0} MB ({1} assets)" -f $menu.Sum.MB, $menu.Sum.Files)
Write-Host ""
Write-Host ("1x1-only (not pulled by Game/MainMenu): {0} MB ({1} assets)" -f $sOnly1x1.MB, $sOnly1x1.Files)
Write-Host ("Game-only (not pulled by 1x1/MainMenu): {0} MB ({1} assets)" -f $sOnlyGame.MB, $sOnlyGame.Files)
Write-Host ("1x1 shared with Game: {0} MB ({1} assets)" -f $sShared.MB, $sShared.Files)
Write-Host ""
Write-Host "Note: WebGL build compresses and deduplicates; incremental cost of 1x1 ~= 1x1-only assets."

Write-Host "`nTop 1x1-only assets:"
$rows = @()
foreach ($g in $only1x1) {
    if (-not $guidToPath.ContainsKey($g)) { continue }
    $p = $guidToPath[$g]
    if (-not (Test-Path $p)) { continue }
    $item = Get-Item $p
    if ($item.PSIsContainer) { continue }
    $rows += [pscustomobject]@{ MB = [math]::Round($item.Length / 1MB, 2); Path = $p.Substring($root.Length + 1) }
}
$rows | Sort-Object MB -Descending | Select-Object -First 20 | Format-Table -AutoSize

# Count prefab instances in 1x1
$sceneText = Get-Content (Join-Path $root "Assets/Scenes/1x1.unity") -Raw
$prefabCount = ([regex]::Matches($sceneText, 'm_SourcePrefab:')).Count
Write-Host "1x1 prefab instances in scene: $prefabCount"
