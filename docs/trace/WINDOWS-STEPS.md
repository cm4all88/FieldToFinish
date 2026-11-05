# Build TRACE and test it in Civil 3D 2024 (Windows)

Needs: Windows, Civil 3D 2024, the .NET 8 SDK (https://dotnet.microsoft.com/download) and the repository anywhere you like (for example on D:).

1. Close Civil 3D.
2. Open PowerShell in the repository folder and run:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\Run-TraceBaseline.ps1
```

It checks prerequisites, builds TRACE, runs the non-CAD tests, installs the development bundle, and puts every log plus a zip in `logs\trace-baseline-<time>\`. It never starts Civil 3D or opens a drawing.

3. Follow `docs\trace\MANUAL-VERIFICATION.md` by hand in Civil 3D.
4. Send me the zip and your notes.

Options: `-Civil3DPath "<folder>"` if Civil 3D is in an unusual place, `-SkipTests`, and `-RunSmoke` (adds the existing headless drafting test; uses no Civil 3D window).

Internal Parametrix edition (separate, optional):

```powershell
dotnet build src\FieldCodes.Cad\FieldCodes.Cad.csproj -p:TraceEdition=Parametrix
powershell -ExecutionPolicy Bypass -File .\deploy\install-dev.ps1 -Edition Parametrix
```
