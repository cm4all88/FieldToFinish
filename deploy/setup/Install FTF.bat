@echo off
rem ============================================================================
rem  Field to Finish -- installer for one person's Civil 3D.
rem
rem  Double-click this. Civil 3D must be closed. No admin rights are needed: the
rem  plugin goes in your own profile, and your office settings are left alone.
rem
rem  Everything is copied to a local folder first, so it also works when this
rem  setup folder is on the network and nothing breaks if the network hiccups.
rem ============================================================================

setlocal
title Install Field to Finish

set "STAGE=%TEMP%\FTF-Setup"

echo.
echo  Installing Field to Finish...
echo.

if exist "%STAGE%" rmdir /s /q "%STAGE%"
robocopy "%~dp0." "%STAGE%" /E /R:2 /W:2 /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 goto copyfailed

powershell -NoProfile -ExecutionPolicy Bypass -File "%STAGE%\install.ps1"
set RESULT=%ERRORLEVEL%

rmdir /s /q "%STAGE%" 2>nul

if not "%RESULT%"=="0" goto failed
echo  Press any key to close this window.
pause >nul
endlocal
exit /b 0

:copyfailed
echo.
echo  Could not copy the setup files from:
echo    %~dp0
echo.
echo  If this is a network folder, check that you can open it, then try again.
echo.
pause >nul
endlocal
exit /b 1

:failed
echo  Nothing was installed. Read the message above, then try again.
echo.
echo  Press any key to close this window.
pause >nul
endlocal
exit /b 1
