# SmokeHarnessCompileCheck

Compiles the Civil 3D harnesses that `deploy\smoke-test.ps1` compiles, the way it compiles them:

- **C# 5**, the language of the .NET Framework `csc.exe` the smoke test uses (no string
  interpolation, no `?.`, no expression-bodied members, no auto-property initialisers);
- against **accoremgd, acdbmgd and acmgd** from Autodesk's real AutoCAD 2024 reference assemblies
  (the `AutoCAD.NET` 24.3.0 NuGet package) and **AeccDbMgd**, here the Civil 3D stand-ins
  (`../CadCompileCheck/Stubs/Civil.cs`, built as their own assembly in `CivilStubs/` because they
  use newer C#).

```
dotnet build tests/SmokeHarnessCompileCheck
```

A harness that does not compile in the smoke test fails every UPDATEFTF (for the required stages)
or turns its check into a warning that proves nothing (for the reported ones). This finds it on any machine first.
`CivilApplication` is the usual trap: it lives in AeccMgd.dll, which the smoke test does not
reference, so the harnesses use `CivilDocument.GetCivilDocument(db)`.

`DeploymentContractTests.EveryHarnessTheSmokeTestCompilesIsCompileCheckedAndAvoidsAeccMgd` keeps the
list in `SmokeHarnessCompileCheck.csproj` in step with the script.

What it cannot catch: a Civil 3D member the stand-ins get wrong, and anything that only shows at
run time. Only the smoke test itself, in Civil 3D, proves a harness works.
