@echo off
rem ============================================================================
rem  Removes Field to Finish from this computer's Civil 3D.
rem  Your office settings (%APPDATA%\FieldToFinish) are kept.
rem ============================================================================

setlocal
title Uninstall Field to Finish

echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Uninstall
echo.
echo  Press any key to close this window.
pause >nul
endlocal
