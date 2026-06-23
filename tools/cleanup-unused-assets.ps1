# Removes unused animation FBX and audio files identified by controller/script audit.
$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $ProjectRoot

function Remove-AssetPath([string]$RelativePath) {
    $full = Join-Path $ProjectRoot $RelativePath
    if (Test-Path $full) {
        Remove-Item -LiteralPath $full -Force -Recurse
        Write-Host "Removed $RelativePath"
    }
}

# --- Unused animation FBX (not referenced by player animator controllers) ---
$controllers = @(
    "Assets\Prefabs\Player\SyntyLocomotion.controller",
    "Assets\Prefabs\Player\SyntyHolsteredLocomotion.controller",
    "Assets\Prefabs\Player\SyntyRemoteLocomotion.controller"
)
$guids = New-Object System.Collections.Generic.HashSet[string]
foreach ($c in $controllers) {
    Select-String -Path $c -Pattern "guid: ([a-f0-9]{32})" -AllMatches |
        ForEach-Object { $_.Matches } |
        ForEach-Object { [void]$guids.Add($_.Groups[1].Value) }
}
$guidToPath = @{}
Get-ChildItem "Assets" -Recurse -Filter "*.meta" | ForEach-Object {
    $lines = Get-Content $_.FullName -TotalCount 2
    if ($lines[1] -match "^guid: ([a-f0-9]{32})") {
        $rel = $_.FullName.Substring($ProjectRoot.Length + 1).Replace("\", "/")
        $asset = $rel.Substring(0, $rel.Length - 5)
        $guidToPath[$matches[1]] = $asset
    }
}
$usedAnim = New-Object System.Collections.Generic.HashSet[string]
foreach ($g in $guids) {
    if ($guidToPath.ContainsKey($g)) {
        $p = $guidToPath[$g]
        if ($p -like "*.fbx") { [void]$usedAnim.Add($p.ToLower()) }
    }
}
$animRoots = @("Assets/Blink", "Assets/Opsive", "Assets/Animations")
$deletedAnim = 0
foreach ($root in $animRoots) {
    if (-not (Test-Path $root)) { continue }
    Get-ChildItem $root -Recurse -Filter "*.fbx" -File | ForEach-Object {
        $rel = $_.FullName.Substring($ProjectRoot.Length + 1).Replace("\", "/")
        if (-not $usedAnim.Contains($rel.ToLower())) {
            Remove-Item $_.FullName -Force
            $meta = "$($_.FullName).meta"
            if (Test-Path $meta) { Remove-Item $meta -Force }
            $deletedAnim++
        }
    }
}
Write-Host "Deleted unused animation FBX: $deletedAnim"

# --- Whole unused audio folders ---
$audioFolders = @(
    "Assets/Free UI Click Sound Effects Pack",
    "Assets/Sounds",
    "Assets/Footsteps - Essentials/Footsteps_DirtyGround",
    "Assets/Footsteps - Essentials/Footsteps_Grass",
    "Assets/Footsteps - Essentials/Footsteps_Gravel",
    "Assets/Footsteps - Essentials/Footsteps_Leaves",
    "Assets/Footsteps - Essentials/Footsteps_Metal",
    "Assets/Footsteps - Essentials/Footsteps_Mud",
    "Assets/Footsteps - Essentials/Footsteps_Sand",
    "Assets/Footsteps - Essentials/Footsteps_Snow",
    "Assets/Footsteps - Essentials/Footsteps_Water",
    "Assets/Footsteps - Essentials/Footsteps_Wood",
    "Assets/Weapons of Choice FREE - Komposite Sound/BULLETS"
)
foreach ($folder in $audioFolders) { Remove-AssetPath $folder }

# --- Keep-only audio in partially used folders ---
$keepAudio = @(
    "Assets/Footsteps - Essentials/Footsteps_Rock/Footsteps_Rock_Walk/Footsteps_Rock_Walk_01.wav",
    "Assets/Footsteps - Essentials/Footsteps_Rock/Footsteps_Rock_Walk/Footsteps_Rock_Walk_02.wav",
    "Assets/Footsteps - Essentials/Footsteps_Rock/Footsteps_Rock_Walk/Footsteps_Rock_Walk_03.wav",
    "Assets/Footsteps - Essentials/Footsteps_Rock/Footsteps_Rock_Walk/Footsteps_Rock_Walk_04.wav",
    "Assets/Footsteps - Essentials/Footsteps_Rock/Footsteps_Rock_Walk/Footsteps_Rock_Walk_05.wav",
    "Assets/Footsteps - Essentials/Footsteps_Rock/Footsteps_Rock_Run/Footsteps_Rock_Run_01.wav",
    "Assets/Footsteps - Essentials/Footsteps_Rock/Footsteps_Rock_Run/Footsteps_Rock_Run_02.wav",
    "Assets/Footsteps - Essentials/Footsteps_Rock/Footsteps_Rock_Run/Footsteps_Rock_Run_03.wav",
    "Assets/Footsteps - Essentials/Footsteps_Rock/Footsteps_Rock_Run/Footsteps_Rock_Run_04.wav",
    "Assets/Footsteps - Essentials/Footsteps_Rock/Footsteps_Rock_Run/Footsteps_Rock_Run_05.wav",
    "Assets/Footsteps - Essentials/Footsteps_Rock/Footsteps_Rock_Jump/Footsteps_Rock_Jump_Start_02.wav",
    "Assets/Footsteps - Essentials/Footsteps_Tile/Footsteps_Tile_Walk/Footsteps_Tile_Walk_01.wav",
    "Assets/Footsteps - Essentials/Footsteps_Tile/Footsteps_Tile_Walk/Footsteps_Tile_Walk_02.wav",
    "Assets/Footsteps - Essentials/Footsteps_Tile/Footsteps_Tile_Walk/Footsteps_Tile_Walk_03.wav",
    "Assets/Footsteps - Essentials/Footsteps_Tile/Footsteps_Tile_Walk/Footsteps_Tile_Walk_04.wav",
    "Assets/Footsteps - Essentials/Footsteps_Tile/Footsteps_Tile_Walk/Footsteps_Tile_Walk_06.wav",
    "Assets/Scifi Guns SFX Pack/Gun4_2.wav",
    "Assets/Scifi Guns SFX Pack/Gun2_2.wav",
    "Assets/Scifi Guns SFX Pack/Gun3_1.wav",
    "Assets/Scifi Guns SFX Pack/Gun4_load.wav",
    "Assets/Scifi Guns SFX Pack/Gun5_Load.wav",
    "Assets/Weapons of Choice FREE - Komposite Sound/GUN/aa3f57b1722ad5453354.mp3",
    "Assets/Weapons of Choice FREE - Komposite Sound/GUN/Handling_Gun_01_Clip_Out_SFX.wav",
    "Assets/Weapons of Choice FREE - Komposite Sound/GUN/Handling_Gun_01_Arming_SFX.wav",
    "Assets/Weapons of Choice FREE - Komposite Sound/GUN/Handling_Gun_01_Clip_In_SFX.wav",
    "Assets/Weapons of Choice FREE - Komposite Sound/GUN/Handling_Gun_01_Reload_Sq_SFX.wav",
    "Assets/Weapons of Choice FREE - Komposite Sound/GUN/JDSherbert - Firearm FX - Weapon SFX Pack - Walther P38 - Equip - 1.mp3",
    "Assets/Weapons of Choice FREE - Komposite Sound/GUN/JDSherbert - Firearm FX - Weapon SFX Pack - Walther P38 - Fire (Dry) - 1.mp3",
    "Assets/Weapons of Choice FREE - Komposite Sound/GUN/JDSherbert - Firearm FX - Weapon SFX Pack - Walther P38 - Fire - 1.mp3",
    "Assets/Weapons of Choice FREE - Komposite Sound/GUN/JDSherbert - Firearm FX - Pistol SFX Pack - Desert Eagle - Equip - 1.mp3",
    "Assets/Weapons of Choice FREE - Komposite Sound/GUN/JDSherbert - Firearm FX - Pistol SFX Pack - Desert Eagle - Fire (Dry) - 1.mp3",
    "Assets/Weapons of Choice FREE - Komposite Sound/GUN/JDSherbert - Firearm FX - Pistol SFX Pack - Desert Eagle - Fire - 1.mp3",
    "Assets/Weapons of Choice FREE - Komposite Sound/GUN/JDSherbert - Firearm FX - Pistol SFX Pack - Desert Eagle - Reload - 1.mp3",
    "Assets/Weapons of Choice FREE - Komposite Sound/mp7/pull.mp3",
    "Assets/Weapons of Choice FREE - Komposite Sound/mp7/insert.mp3",
    "Assets/Weapons of Choice FREE - Komposite Sound/reloading-weapon1.mp3",
    "Assets/Resources/Sounds/music.mp3",
    "Assets/Resources/Sounds/rain.mp3",
    "Assets/Resources/Sounds/start.wav",
    "Assets/Resources/Sounds/cancel.wav",
    "Assets/Resources/Sounds/buttons.wav",
    "Assets/Resources/Sounds/case.wav",
    "Assets/Resources/Sounds/case2.wav",
    "Assets/Resources/Sounds/1.mp3",
    "Assets/Resources/Sounds/2.mp3",
    "Assets/Resources/Sounds/3.wav",
    "Assets/Resources/Sounds/4.mp3",
    "Assets/Resources/Sounds/swim1.mp3",
    "Assets/Resources/Sounds/swim2.mp3",
    "Assets/Resources/Sounds/swim3.mp3",
    "Assets/Resources/Sounds/swim4.mp3",
    "Assets/Resources/BRAudio/PlaneJump.wav"
) | ForEach-Object { $_.Replace("\", "/").ToLower() }
$keepSet = New-Object System.Collections.Generic.HashSet[string]
foreach ($k in $keepAudio) { [void]$keepSet.Add($k) }

$audioRoots = @(
    "Assets/Footsteps - Essentials/Footsteps_Rock",
    "Assets/Footsteps - Essentials/Footsteps_Tile",
    "Assets/Scifi Guns SFX Pack",
    "Assets/Weapons of Choice FREE - Komposite Sound/GUN",
    "Assets/Weapons of Choice FREE - Komposite Sound/mp7",
    "Assets/Resources/Sounds",
    "Assets/Resources/BRAudio"
)
$deletedAudio = 0
foreach ($root in $audioRoots) {
    if (-not (Test-Path $root)) { continue }
    Get-ChildItem $root -Recurse -Include *.wav,*.mp3,*.ogg -File | ForEach-Object {
        $rel = $_.FullName.Substring($ProjectRoot.Length + 1).Replace("\", "/").ToLower()
        if (-not $keepSet.Contains($rel)) {
            Remove-Item $_.FullName -Force
            $meta = "$($_.FullName).meta"
            if (Test-Path $meta) { Remove-Item $meta -Force }
            $deletedAudio++
        }
    }
}
Write-Host "Deleted unused audio files: $deletedAudio"

# --- Remove empty Blink Combat/Gathering folders if any files remain ---
Remove-AssetPath "Assets/Blink/Art/Animations/Animations_Starter_Pack/Combat"
Remove-AssetPath "Assets/Blink/Art/Animations/Animations_Starter_Pack/Gathering"

Write-Host "Cleanup complete."
