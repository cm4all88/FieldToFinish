# CadCompileCheck

Compiles the FTFRECORD side of `FieldCodes.Cad` against hand-written stand-ins for the
AutoCAD, Civil 3D and Windows Runtime types it uses, so the plugin code gets a compiler
on a machine with no Civil 3D (Linux, a CI runner, a laptop without Autodesk). Nothing here
runs: the stubs return nothing and draw nothing. What the check catches is the class of
mistake a compiler catches -- a misspelt member, a wrong argument order, a method that hides
a WinForms event, an `internal` helper called from the wrong assembly, a warning that the
real project (which treats warnings as errors) would refuse.

```
dotnet build tests/CadCompileCheck
```

Same language version (C# 7.3), same target (net48 reference assemblies), same
warnings-as-errors and documentation settings as `src/FieldCodes.Cad`.

## Scope

The five FTFRECORD files and the plugin helpers they lean on:

| Compiled from source | Stubbed (`Stubs/Internal.cs`) |
|---|---|
| RecordCommands, RecordDrafter, RecordDocumentReader, Ui/RecordReviewForm, Setup/RecordSurveyPage | `EasementInspectCommands` (three members), `EasementCommands.Extract`, `InspectRow`, the `DipBuilderForm` palette, `ThemePreference` |
| Ownership, DrawingStore, CadUtil, FtfSession, TextMask, Setup/DrawingResources, Setup/SetupPage | |

`Stubs/Geometry.cs` and `Stubs/Database.cs` follow the real AcDbMgd / AcMgd / AeccDbMgd
signatures; `Stubs/WinRt.cs` follows the Windows 10 SDK contracts. When the plugin starts
using a member the stubs lack, the build says so: add the member with the real signature
(check the Autodesk docs, do not guess) rather than changing the plugin to fit the stub.

## What it cannot tell you

Whether the code works in Civil 3D. Transaction discipline, open modes, label styles, WinRT
threading and everything else about the running product are still UNTESTED until the DLL
is loaded into Civil 3D 2024 on Windows. A green build here is the floor, not the ceiling.
