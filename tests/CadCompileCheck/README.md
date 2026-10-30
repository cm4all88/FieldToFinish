# CadCompileCheck

Compiles the whole of `FieldCodes.Cad` against hand-written stand-ins for the AutoCAD,
Civil 3D, ribbon (AdWindows) and Windows Runtime types it uses, so the plugin code gets a
compiler on a machine with no Civil 3D (Linux, a CI runner, a laptop without Autodesk). Nothing here
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

Every `.cs` file under `src/FieldCodes.Cad` (bin and obj excepted), picked up by a wildcard,
so a file a branch adds is compiled without anyone listing it. The point is the merge: when
feature branches come together on `integration`, a call into plugin code whose signature
another branch changed, a member that went missing, or two branches defining the same type
fails here, on Linux, before anyone builds on Windows. Nothing in the plugin is stubbed;
only Autodesk's assemblies and the Windows Runtime are.

| Stub file | Stands in for |
|---|---|
| `Stubs/Database.cs` | AcDbMgd / AcMgd / AcCoreMgd: database, entities, symbol tables, plotting, editor and prompts, documents and their events, transient graphics; the Civil 3D (AeccDbMgd) points, styles, alignments and surfaces |
| `Stubs/Geometry.cs` | The Ge types (points, vectors, matrices, curves), plus `Extents2d` / `Extents3d`, which AcDbMgd declares in DatabaseServices |
| `Stubs/Extra.cs` | The AdWindows ribbon, `ProgressMeter`, xref spatial filters, regions, `CompositeCurve3d`, Civil 3D pipes and structures |
| `Stubs/WinRt.cs` | The Windows 10 SDK contracts (OCR, PDF rendering) used by FTFRECORD |

The stubs follow the real AcDbMgd / AcMgd / AcCoreMgd / AdWindows / AeccDbMgd signatures
(namespace, declaring type, parameter types, return type, static or instance) for the
members the plugin uses -- the surface members `TinSurface.ExtractContoursAt`,
`GetGeneralProperties`, `GetTinProperties`, `GetTriangles` and `IsOutOfDate` as the Civil 3D
API reference documents them; `FeatureLinePointType` in `Autodesk.Civil` and
`PointDisplayStyleType` outside it, as the first real FTFCOMPARE build found. When the plugin
starts using a member the stubs lack, the build says so: add the member with the real
signature (check the Autodesk docs, do not guess) rather than changing the plugin to fit the
stub. A missing type or member in a `FieldCodes.*` namespace is never a stub gap -- that is
the plugin itself not compiling, and the fix belongs in `src/`.

## What it cannot tell you

Whether the code works in Civil 3D. Transaction discipline, open modes, label styles, WinRT
threading and everything else about the running product are still UNTESTED until the DLL
is loaded into Civil 3D 2024 on Windows. A green build here is the floor, not the ceiling.
