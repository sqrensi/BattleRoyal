@echo off
setlocal
cd /d "%~dp0"
for %%I in ("My project (12).exe" "ShooterPrototype.exe" "BattleRoyal.exe" "Game.exe") do (
  if exist %%~I (
    start "" "%%~fI" -clientIndex 4
    exit /b 0
  )
)
echo Game exe not found in %CD%
pause
exit /b 1
