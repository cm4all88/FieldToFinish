# CivilApiNames

Makes the compile checks know every type name Civil 3D 2024 declares, so a name Civil 3D shares
with AutoCAD or .NET fails on any machine, before a push, instead of in UPDATEFTF's Windows build.

Autodesk does not publish Civil 3D's assemblies, so the checks that run without Civil 3D
(`tests/CadCompileCheck`, `tests/RealApiCompileCheck`, `tests/SmokeHarnessCompileCheck`) only
know the Civil types written into `tests/CadCompileCheck/Stubs/Civil.cs` and
`CivilShadows.cs`. `Autodesk.Civil.DatabaseServices.DBObject` was not one of them, and a bare
`DBObject` in `Accountability.cs` stopped UPDATEFTF at fb3d1b2 (CS0104).

This tool reads the public type names of `AeccDbMgd.dll` and `AecBaseMgd.dll` from an installed
Civil 3D 2024 and writes `tests/CadCompileCheck/Stubs/CivilApiNames.g.cs`: one empty placeholder
per type the hand-written stand-ins do not declare. The three compile checks take that file
when it exists.

- It reads metadata only (System.Reflection.Metadata). Nothing of Autodesk's is loaded or run.
- No Autodesk file is copied: the output holds type names, which Autodesk's API reference
  publishes, and no member or code.
- It is not in `FieldToFinish.sln`, and nothing from it ships.

## Run it (once per Civil 3D version)

On a machine with Civil 3D 2024, in the working copy:

```powershell
cd C:\dev\FieldToFinish
dotnet run -c Release --project tools\CivilApiNames
```

If Civil 3D is not under `C:\Program Files\Autodesk\AutoCAD 2024`:

```powershell
dotnet run -c Release --project tools\CivilApiNames -- -C3D2024 "D:\Autodesk\AutoCAD 2024"
```

Then run the checks and commit the file (or send it to whoever pushes):

```powershell
dotnet build tests\CadCompileCheck
dotnet build tests\RealApiCompileCheck
dotnet build tests\SmokeHarnessCompileCheck
git add tests\CadCompileCheck\Stubs\CivilApiNames.g.cs
```

If a check fails after this:

- **In a plugin file:** a bare name Civil 3D declares too. Name the Civil type through the alias
  (`CivDb.X`), or the other one in full. This is the failure the tool exists to find.
- **In `Civil.cs`:** Civil 3D declares a name that the stand-ins use for an AutoCAD type
  (`ObjectId`, `Curve`...). Write the AutoCAD type in full there
  (`Autodesk.AutoCAD.DatabaseServices.Curve`).

## What it still does not check

Civil 3D **members**: a property or method that the stand-ins declare differently from
AeccDbMgd, or that does not exist. Only names are generated. Those are still checked only by the
Windows build, which UPDATEFTF runs before it installs anything.

## Options

| Option | Default |
|---|---|
| `-C3D2024 <folder>` | `C:\Program Files\Autodesk\AutoCAD 2024` (reads `C3D\AeccDbMgd.dll` and `AecBaseMgd.dll`) |
| `-Out <file>` | `tests/CadCompileCheck/Stubs/CivilApiNames.g.cs` |
| `-Prefix <namespace>` | `Autodesk.Civil` and `Autodesk.Aec`; repeat for more |
| `<assembly>...` | Read these instead of the two Civil 3D assemblies |
