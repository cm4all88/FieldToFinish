@echo off
rem ============================================================================
rem  Field to Finish -- put back the FTF that was installed before the last
rem  install or update (kept in %LOCALAPPDATA%\FieldToFinish\backups).
rem
rem  Close Civil 3D first. Your settings stay as they are now.
rem ============================================================================

setlocal
title Restore previous Field to Finish

echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -RestorePrevious
echo.
echo  Press any key to close this window.
pause >nul
endlocal
