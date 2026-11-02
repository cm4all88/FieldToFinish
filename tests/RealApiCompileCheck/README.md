# RealApiCompileCheck

Compiles the whole of `FieldCodes.Cad` against the **real** AutoCAD 2024 API: Autodesk's own
reference assemblies from the `AutoCAD.NET` 24.3.0 NuGet package (AcDbMgd, AcCoreMgd, AcMgd,
AdWindows -- AutoCAD R24.3, which Civil 3D 2024 is built on) and the real
`Microsoft.Windows.SDK.Contracts`. Same language version, target, warnings-as-errors and
documentation settings as `src/FieldCodes.Cad`.

```
dotnet build tests/RealApiCompileCheck
```

Needs nuget.org once to restore; nothing from it runs or ships. It is deliberately not in
`FieldToFinish.sln`: the Windows build compiles against the installed Civil 3D anyway.

## Why it exists

`tests/CadCompileCheck` uses hand-written stand-ins that declare only the Autodesk types the
plugin uses. A name both an Autodesk namespace and a .NET namespace declare is then not
ambiguous there, but is in the real build. That is how `Font` in `Setup/LayersPage.cs`
(`Autodesk.AutoCAD.DatabaseServices.Font` and `System.Drawing.Font`, error CS0104) passed the
stand-in check and stopped UPDATEFTF at cf3e6f4. This project fails on it.

## What is still a stand-in

Civil 3D's `AeccDbMgd`, which Autodesk does not publish: `../CadCompileCheck/Stubs/Civil.cs`
(shared with CadCompileCheck). Its classes that derive from AutoCAD database types call the
constructor the real AcDbMgd declares, so they compile against the real AutoCAD here.
A Civil 3D member the stand-in gets wrong is still only caught by the Windows build.

## The quick check that needs nothing

`tests/FieldCodes.Tests/AmbiguousTypeNameTests.cs` runs with the unit tests on any machine.
It holds every type name the real AutoCAD 2024 assemblies share with the .NET namespaces the
plugin imports (found by reflection over those assemblies) and fails on a bare use of one in a
file that imports both sides. This project remains the authority.

## Before pushing a branch for UPDATEFTF

```
dotnet test tests/FieldCodes.Tests
dotnet build tests/CadCompileCheck
dotnet build tests/RealApiCompileCheck
```
