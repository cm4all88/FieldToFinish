# Plan: commercial assembly `TRACE.Cad.dll` (do during the Windows phase)

Nothing is renamed yet. This lists every reference found by searching the whole repo for `FieldCodes.Cad`, and what changes.

## Approach
Make the assembly name depend on the edition, so the internal build is byte-for-byte unaffected:

| Edition | `AssemblyName` | DLL |
|---|---|---|
| `Parametrix` (internal) | `FieldCodes.Cad` (unchanged) | `FieldCodes.Cad.dll` |
| `Default` (TRACE) | `TRACE.Cad` | `TRACE.Cad.dll` |

Project folder, project name, solution entry, `RootNamespace` and every C# `namespace FieldCodes.Cad` stay as they are. Type names used by reflection in the harnesses (`FieldCodes.Cad.FtfSession`, `...DipSession`, `...DrawingStore`, `...Ui.DipBuilderForm`) are namespace names, so they keep working.

## Already safe
- `[assembly: ExtensionApplication(typeof(FieldCodes.Cad.FtfRibbon))]` uses the type, not the file name.
- Settings-folder choice (`Branding`) reads a metadata stamp, not the assembly name.
- Nothing in `src/` loads the plugin assembly by name (`Assembly.Load("FieldCodes.Cad")` is not used).

## Changes needed
| File | Reference | Change |
|---|---|---|
| `src/FieldCodes.Cad/FieldCodes.Cad.csproj:23` | `<AssemblyName>FieldCodes.Cad</AssemblyName>` | `TRACE.Cad` when `TraceEdition` is `Default`, else unchanged |
| `deploy/TRACE.bundle/PackageContents.xml:29` | `ModuleName="./Contents/2024/FieldCodes.Cad.dll"` | `TRACE.Cad.dll` |
| `deploy/FieldToFinish.bundle/PackageContents.xml:41` | same | **leave** (internal) |
| `deploy/install-dev.ps1:73` | `Test-Path ... 'FieldCodes.Cad.dll'` | check for the DLL that matches `-Edition` |
| `deploy/build-release.ps1` | project path only; copies whatever the build produced | none (manifest must match the new DLL name) |
| `tests/LiveSmoke/CleanupTestHarness.cs:41`, `ProductionTestHarness.cs:43`, `tests/RealWorld/RealWorldHarness.cs:37`, `tests/LiveSmoke/DipUiTestHarness.cs:223` | `a.GetName().Name == "FieldCodes.Cad"` | accept `FieldCodes.Cad` or `TRACE.Cad` |
| `tests/LiveSmoke/runprod.scr:1`, `runprod2.scr:1`, `runcleanup.scr:1`, `rundraft.scr:2` | `NETLOAD "C:\dev\FieldToFinish\...\FieldCodes.Cad.dll"` | internal: unchanged; for TRACE runs, pass the DLL name as a parameter |
| `tests/RealWorld/baseline-png.ps1:13`, `report-only.ps1:16`, `run-real.ps1:30,55` | `$bin\FieldCodes.Cad.dll` | add a `-Dll` parameter, default `FieldCodes.Cad.dll` |
| `tests/LiveSmoke/draft` harness | reads XData by hand, no reference | none |

Excluded-module harnesses (DIP, production/easement) stay internal-only and are not part of TRACE testing.

## Order of work
1. Verify the build, install and workflows with the current names (WINDOWS-STEPS.md).
2. Apply the table above in one commit; rebuild both editions.
3. Verify both: internal `FTF` still loads `FieldCodes.Cad.dll`; TRACE loads `TRACE.Cad.dll`, uses `%APPDATA%\TRACE`, and the bundle starts Civil 3D without NETLOAD.
