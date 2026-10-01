@echo off
rem ============================================================================
rem  Field to Finish -- why isn't it showing up?
rem
rem  Double-click this. It changes nothing: it reads the computer and writes
rem  ftf-check.txt, which tells the drafting lead exactly what is wrong.
rem ============================================================================

setlocal
title Check Field to Finish

echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0check.ps1"
echo.
echo  Press any key to close this window.
pause >nul
endlocal
