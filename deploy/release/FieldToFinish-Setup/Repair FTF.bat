@echo off
rem ============================================================================
rem  Field to Finish -- make Civil 3D look at FTF again.
rem
rem  Civil 3D keeps its own note about every plugin it has seen. If that note is
rem  stale -- left pointing at files that went away, or switched off after a
rem  failed load -- Civil 3D ignores FTF no matter how many times it is
rem  reinstalled. This deletes the note. Civil 3D writes a fresh one the next
rem  time it starts. Nothing else is touched, and FTF itself is not reinstalled.
rem
rem  Close Civil 3D first, then double-click this, then start Civil 3D.
rem ============================================================================

setlocal
title Repair Field to Finish

echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0check.ps1" -Repair
echo.
echo  Now start Civil 3D and type FTF.
echo.
echo  Press any key to close this window.
pause >nul
endlocal
