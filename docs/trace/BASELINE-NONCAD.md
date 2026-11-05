# Non-CAD baseline (Linux, .NET 8 SDK) — 2026-11-05

Command: `dotnet test tests/FieldCodes.Tests` (FieldCodes netstandard2.0 library + 947 xUnit tests; the Civil 3D project is not built here because it needs the Autodesk assemblies).

Result: **943 passed, 4 failed, 0 skipped.**

The 4 failures are platform artefacts, not product defects: `ReportSitsBesideTheDrawing` (Exception report, Unhandled summary, Linework inventory CSV) feeds Windows paths (`C:\jobs\...`) to `Path.GetDirectoryName`, which does not split backslashes on Linux. They should pass on Windows; confirm in the Windows baseline. Do not "fix" the product code for this.

Not covered: everything in `FieldCodes.Cad` (needs Windows + Civil 3D 2024). Windows baseline is still required.

Environment note: dotnet installed via `apt-get install dotnet-sdk-8.0`; NuGet restore works; dot.net download hosts are blocked.

## Update after configuration separation
The four path tests were made portable (paths built with `Path.Combine`; product code unchanged). The fixture still tests the Parametrix grammar from `config/internal/parametrix/rules.json`; the neutral default is covered by `DefaultProfileTests` and `StandardsImportTests`.
