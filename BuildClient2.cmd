@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\build_client2_launcher.ps1"
if errorlevel 1 (
  echo.
  echo Failed to build LaunchClient2.exe
  pause
  exit /b 1
)
echo.
echo LaunchClient2.exe copied to Build\Server\
pause
