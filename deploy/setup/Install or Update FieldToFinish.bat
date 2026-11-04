@echo off
rem ============================================================================
rem  Field to Finish -- install or update, for one person's Civil 3D 2024.
rem
rem  Double-click this. It installs FTF if it is not here yet, updates it when
rem  this approved release is different from the one installed, repairs a damaged
rem  install, and does nothing when FTF is already this release.
rem
rem  No admin rights: FTF goes in your own profile, and Civil 3D loads it by
rem  itself every time it starts (no NETLOAD). Office standards and your own
rem  settings are never touched.
rem
rem  Everything is copied to a local folder first and checked file by file, so a
rem  network hiccup can never leave a half-installed FTF. If anything goes wrong
rem  the FTF you had is put back. A log is kept in
rem  %LOCALAPPDATA%\FieldToFinish\logs.
rem ============================================================================

setlocal
title Install or Update Field to Finish

set "STAGE=%TEMP%\FTF-Setup"

echo.
echo  Field to Finish -- install or update
echo  Approved release: %~dp0
echo.

tasklist /FI "IMAGENAME eq acad.exe" 2>nul | find /I "acad.exe" >nul
if not errorlevel 1 goto civilopen

if not exist "%~dp0Bundle\PackageContents.xml" goto notrelease
if not exist "%~dp0install.ps1" goto notrelease

if exist "%STAGE%" rmdir /s /q "%STAGE%"
rem The release only: not the releases it replaced, nor one being published right now.
robocopy "%~dp0." "%STAGE%" /E /XD "Previous releases" "_publishing" /R:2 /W:2 /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 goto copyfailed

powershell -NoProfile -ExecutionPolicy Bypass -File "%STAGE%\install.ps1" -Source "%~dp0."
set RESULT=%ERRORLEVEL%

rmdir /s /q "%STAGE%" 2>nul

if not "%RESULT%"=="0" goto failed
echo  Press any key to close this window.
pause >nul
endlocal
exit /b 0

:civilopen
echo  Civil 3D is open.
echo.
echo  Save your drawings and close every Civil 3D window, then double-click
echo  this file again. Civil 3D holds FTF open while it runs, so FTF cannot be
echo  replaced underneath it. Nothing was changed.
echo.
echo  Press any key to close this window.
pause >nul
endlocal
exit /b 1

:notrelease
echo  This folder is not a complete Field to Finish release (the Bundle folder
echo  or install.ps1 is missing). Nothing was changed.
echo.
echo  If a new release is being published right now, wait a minute and
echo  double-click this again.
echo.
echo  Run "Install or Update FieldToFinish.bat" from the approved release
echo  folder itself, or tell the drafting lead.
echo.
echo  Press any key to close this window.
pause >nul
endlocal
exit /b 1

:copyfailed
echo.
echo  Could not copy the release from:
echo    %~dp0
echo.
echo  If this is a network folder, check that you can open it (network, VPN),
echo  then try again. The FTF already on this computer is unchanged.
echo.
echo  Press any key to close this window.
pause >nul
endlocal
exit /b 1

:failed
echo  Read the message above. If FTF was not installed, the FTF you had (if
echo  any) is unchanged or was put back. Send the log it names to the drafting
echo  lead if it happens again.
echo.
echo  Press any key to close this window.
pause >nul
endlocal
exit /b 1
