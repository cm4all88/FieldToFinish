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

Civil 3D's `AeccDbMgd` and `AecBaseMgd`, which Autodesk does not publish:
`../CadCompileCheck/Stubs/Civil.cs` (the Civil types the plugin uses) and `CivilShadows.cs` (Civil
names known to clash with AutoCAD's or .NET's), shared with CadCompileCheck; plus
`CivilApiNames.g.cs` when it has been generated. Their classes that derive from AutoCAD database
types call the constructor the real AcDbMgd declares, so they compile against the real AutoCAD here.

So this check is real for AutoCAD and partial for Civil 3D:

| | Checked against |
|---|---|
| AutoCAD 2024 types, members, names | The real reference assemblies |
| A Civil 3D name that clashes with AutoCAD or .NET | `CivilShadows.cs` (known clashes); every Civil name once `tools/CivilApiNames` output is committed. Files outside the lint's older-files list must use aliases, so they cannot clash at all |
| Civil 3D members (properties, methods, signatures) | Stand-ins only. **Only the Windows build checks them** (UPDATEFTF stops before installing) |

`DBObject` at fb3d1b2 is why the Civil rows exist: Civil.cs did not declare Civil 3D's own
`DBObject`, so the bare name in `Accountability.cs` was not ambiguous here and was on Windows.
Built against fb3d1b2 now, this project fails with the same two errors (361,55 and 478,44).

## The quick check that needs nothing

`tests/FieldCodes.Tests/AmbiguousTypeNameTests.cs` runs with the unit tests on any machine.
It holds every type name the real AutoCAD 2024 assemblies share with the .NET namespaces the
plugin imports (found by reflection over those assemblies) and fails on a bare use of one in a
file that imports both sides. This project remains the authority.

## Before pushing a branch for UPDATEFTF

```
tools/check-before-push.sh
```

runs the namespace lint, the three compile checks, the unit tests (on this checkout and again on a
CRLF checkout of the commit, as Git for Windows and UPDATEFTF's build checkout have the files) and
the PowerShell lint, and says whether Civil 3D names were checked in full. To run it on every push from a working copy:

```
git config core.hooksPath tools/githooks
```

The four steps by hand:

```
dotnet test tests/FieldCodes.Tests
dotnet build tests/CadCompileCheck
dotnet build tests/RealApiCompileCheck
dotnet build tests/SmokeHarnessCompileCheck
```
