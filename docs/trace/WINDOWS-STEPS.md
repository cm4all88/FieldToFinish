# Install TRACE (Windows, Civil 3D 2024)

Needs: Windows, Civil 3D 2024, the .NET 8 SDK (https://dotnet.microsoft.com/download).

1. Close Civil 3D.
2. In PowerShell, in the repository folder:

```powershell
git pull
powershell -ExecutionPolicy Bypass -File .\deploy\Install-Trace.ps1
```

3. Start Civil 3D. The TRACE tab and the TRACE palette are there.

To remove TRACE: `powershell -ExecutionPolicy Bypass -File .\deploy\Install-Trace.ps1 -Uninstall`.

TRACE is independent of FieldToFinish: its own bundle (`TRACE.bundle`), commands (`TRACE...`), stored-data names and settings folder (`%APPDATA%\TRACE`). It runs beside an installed FieldToFinish and never touches it. If Civil 3D is installed somewhere unusual, add `-Civil3DPath "<folder that contains acad.exe>"`.

If the build prints red errors, send me those lines.
