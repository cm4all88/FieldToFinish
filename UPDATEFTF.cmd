@echo off
rem Builds, validates and installs the latest approved Field to Finish (see DEPLOY.md).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0UPDATEFTF.ps1" %*
exit /b %ERRORLEVEL%
