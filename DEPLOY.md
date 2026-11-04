# Updating FTF in Civil 3D

One command builds the current approved Field to Finish, proves it works, and installs it
where Civil 3D loads it automatically:

```
C:\dev\UPDATEFTF
```

Then start (or restart) Civil 3D and type `FTFVERSION`. No branch switching, no NETLOAD, no
copying DLLs.

## Where the approved build comes from: the `integration` branch

`integration` is the one maintained branch of approved FTF functionality. Feature work happens on
its own branches; a feature reaches Civil 3D only by being merged into `integration`. Nothing is
merged into `main` without explicit approval.

What `integration` holds today:

| Came from | What |
|---|---|
| `dip-workflow-simplification` (which already contained the earlier `integration`, `easement-drafting-feedback`, `dip-quick-add-pipe`, `dip-office-label-styles`, `exhibit-template-audit`) | Dip Builder, strip/portion easements, exhibits, legal drafts, FTFRECORD, FTFROWDIM, FTFTILDE, the two ribbon tabs, the surveyor installer and office auto-update (FTFUPDATE) |
| `claude/kind-hamilton-apylzj` | FTFCOMPARE, FTFTRIM |
| `claude/awesome-bohr-xubo32` | FTFROW, FTFLOTS |
| `claude/friendly-curie-0inutv` | FTFCONTOUR, FTFLABELALL, FTFBENCH, FTFLABELDRIVEWAY, note-driven pole/sign labels, fence and legend line-label rules |

Not merged (not approved): `claude/ecstatic-bell-kr80zb` (Crew Upload / Survey Schedule app),
`claude/epic-hopper-fdtp4f` (training course documents), `feature/ftfdip-production-workflow`
(superseded by the later dip work).

Bringing a branch in: merge it into `integration`, resolve conflicts, and run
`dotnet test tests/FieldCodes.Tests`, `dotnet build tests/CadCompileCheck` (which compiles the
whole plugin against API stand-ins, so a bad merge fails before anyone builds on Windows) and
`dotnet build tests/RealApiCompileCheck` (which compiles it against Autodesk's real AutoCAD 2024
reference assemblies from NuGet, catching what stand-ins cannot, such as a type name that is
ambiguous between AutoCAD and System.Drawing), and `dotnet build tests/SmokeHarnessCompileCheck`
(which compiles the Civil 3D harnesses the smoke test compiles, as C# 5 against the same assemblies,
so a harness that would stop every UPDATEFTF fails first). `tools/check-before-push.sh` runs all of
them; `git config core.hooksPath tools/githooks` makes git run it before every push. Then
`UPDATEFTF` picks it up.

None of these has Civil 3D's own assemblies. Plugin files name Civil 3D types through aliases
(`using CivDb = Autodesk.Civil.DatabaseServices;`), and the lint refuses a plain Civil import in a
new file, so a Civil name cannot clash with an AutoCAD one there (`DBObject` stopped UPDATEFTF at
fb3d1b2 that way). Civil 3D *members* are still checked only by the Windows build. Running
`tools/CivilApiNames` once on a Civil 3D machine and committing its output makes the checks know
every Civil 3D type name (`tools/CivilApiNames/README.md`).

## First time on a computer

From PowerShell:

```powershell
cd C:\dev\FieldToFinish

# 1. Save any work that exists only on this machine (uncommitted edits, new files, local
#    commits) to a branch on GitHub. Nothing in the working copy changes.
git fetch origin
git worktree add --detach ..\FieldToFinish-build origin/integration
powershell -ExecutionPolicy Bypass -File ..\FieldToFinish-build\deploy\Save-LocalWork.ps1 -Path C:\dev\FieldToFinish

# 2. Build, validate and install the approved FTF (close Civil 3D first, or it will ask you to).
powershell -ExecutionPolicy Bypass -File ..\FieldToFinish-build\UPDATEFTF.ps1
```

`Save-LocalWork.ps1` lists any files `.gitignore` keeps out of the snapshot; add any you need with
`-Also <path>`. The `wip/...` branch it pushes is reviewed and merged into `integration` like any
other work.

The first `UPDATEFTF` run makes `C:\dev\UPDATEFTF.cmd`, so from then on `C:\dev\UPDATEFTF` is
the whole command.

Stop using NETLOAD on `src\FieldCodes.Cad\bin\Debug`: a second copy of FTF loaded beside the
installed one fights it over every command. `UPDATEFTF` lists anything else that loads an FTF DLL
(old startup entries, the Startup Suite, another bundle, an `acad*.lsp` NETLOAD);
`UPDATEFTF -FixStartup` disables the registry ones after backing them up.

## What UPDATEFTF does

1. **Fetches `origin/integration`** and records its head in
   `%LOCALAPPDATA%\FieldToFinish\latest-approved.json` (FTFVERSION reads it).
2. **Builds that exact commit in its own checkout**, `C:\dev\FieldToFinish-build` (a git worktree
   of your repository, detached at the commit). Your working copy, its branch and its uncommitted
   changes are never touched. Files edited by hand in the build checkout are never discarded:
   UPDATEFTF stops and says so.
3. **Validates before anything installed changes**: the Release build (the DLL must carry the
   commit being built), the full unit-test suite (the regression tests for every FTF feature),
   packaging (refuses a stale DLL), and the installer's own test in a scratch profile.
4. **Waits for Civil 3D to be closed.** If it is open, UPDATEFTF stops with exit code 2 and the
   validated package kept; run it again after closing Civil 3D and it installs in about a minute.
   `-WaitForCivil3D` waits instead.
5. **Backs up the installed FTF** (and its startup registration) to
   `%LOCALAPPDATA%\FieldToFinish\backups`, then **installs** into
   `%APPDATA%\Autodesk\ApplicationPlugins\FieldToFinish.bundle` (an atomic swap: the old copy
   stays until the new one is whole), **registers it to load when Civil 3D starts**, and checks
   every installed file's SHA-256 against the package.
6. **Smoke-tests it inside Civil 3D** (headless accoreconsole, `deploy\smoke-test.ps1`). If that
   fails, the previous FTF is put back automatically and the failed build is kept in
   `%LOCALAPPDATA%\FieldToFinish\failed` for diagnosis.
7. Says what is installed and what to do next.

Only a clean build of `origin/integration` that passed all of that is stamped **approved**.

| Option | |
|---|---|
| `-Status` | Report only: installed build, latest approved build, startup registration, other loaders, whether Civil 3D needs a restart. |
| `-Rollback` | Put back the most recent backup. |
| `-WaitForCivil3D` | Wait for Civil 3D to close instead of stopping. |
| `-Force` | Rebuild and reinstall even if already current. |
| `-Dev -Ref origin/claude/xyz` | Install a development branch for testing; stamped **DEV**, never approved. |
| `-PublishTo \\server\share\CAD\FieldToFinish` | After every check above passed (smoke test included, never with `-SkipSmoke` or `-Dev`), publish the build as the approved release survey staff install from. `-ReleaseNotes "..."` adds a note. See [docs/network-deployment.md](docs/network-deployment.md). |
| `-FixStartup` | Disable other registry startup entries that load an FTF DLL (backed up first). |
| `-SkipSmoke` | Skip the in-Civil 3D test (the install is then unproven). |

Exit codes: `0` installed or already current, `1` failed (installed FTF unchanged or restored),
`2` validated but Civil 3D is open, `3` installed and proven on this workstation but not published
(the release folder is unchanged).

Every run writes a log to `%LOCALAPPDATA%\FieldToFinish\logs`.

## Is Civil 3D running the latest approved build?

Type `FTFVERSION` (or click **FTF Version** on the FTF tab):

```
Field to Finish -- approved build 5c3c021 (integration), built 2026-10-30 14:05 UTC
  commit:    5c3c0218830d94574671bd690a5df4ce559afdd6
  loaded:    C:\Users\...\ApplicationPlugins\FieldToFinish.bundle\Contents\2024\FieldCodes.Cad.dll
  installed: approved build 5c3c021 (integration), built 2026-10-30 14:05 UTC  [...FieldToFinish.bundle]
  latest approved: 5c3c021 "..." (as of UPDATEFTF 2026-10-30 15:00 UTC)
STATUS: running the latest approved build.
```

What else it can say:

- `RESTART:` the installed build is newer than the one this session loaded.
- `UPDATE:` a newer approved build is on `integration`; run UPDATEFTF.
- `WARNING:` FTF was loaded from outside the installed bundle (a NETLOAD of a development DLL).
- `NOTE: a development build`: built by hand or with `-Dev`, not an approved release.

The same line appears on the command line when Civil 3D starts, with any warning. The FTF window's
About page shows it too.

Where the information comes from: every FTF assembly is stamped at build time
(`build\FtfBuildInfo.targets`) with its commit, branch, build date and channel (Windows shows it as
the DLL's *Product version*: `1.0.0+5c3c021.approved`); the installed bundle carries
`build-info.json` with the same stamp and a SHA-256 of every file.

## The smoke test

`deploy\smoke-test.ps1` runs in accoreconsole against the installed DLL:

- **The build under test is the one loaded:** exactly one `FieldCodes.Cad`, from the installed
  bundle, stamped with the commit just built, declaring FTFVERSION, FTFCONTOUR, FTFLABELALL,
  FTFROW, FTFLOTS, FTFCOMPARE, FTFRECORD and FTFDIP. Otherwise it fails at once.
- **Commands are registered and actually run:** the plugin counts every FTFVERSION and FTFCONTOUR
  run (`FtfCommandTrace`). Each check first requires that the command ran exactly once since the
  last check, so an `Unknown command` can never leave later checks passing on nothing. The runner
  also watches the output and kills the run the moment `Unknown command` appears.
- **FTFCONTOUR does its job:** on a seeded TIN surface, a multi-elevation run, one `U` undoing all
  of it, a re-run that replaces instead of duplicating, and a boundary run with labels. Every
  stage also checks that the surface's statistics and every triangle are untouched.

- **Point identity, accountability and annotation styles are required checks**
  (`IdentityTestHarness`, `AccountabilityTestHarness`, `StyleTestHarness`): each on its own fresh
  drawing; a failure in any of them puts the previous build back, and nothing is published.

`tests\LiveSmoke\run-contour.ps1 -Installed` runs the same test against whatever is installed.

### When a run stops early

An exception that nothing catches ends accoreconsole without printing anything, so a log can
stop at a prompt with no error (IDVERIFY READER at 68804df). Each run now leaves enough to say
where and why:

| Where | What |
|---|---|
| The failure message (and the UPDATEFTF log) | How accoreconsole ended: its exit code and what it means (3 = the harness's FATAL, 0xE0434352 = a .NET exception nothing caught, 0xC0000005 = a native crash), or that it was stopped on a timeout, and after how long. Then the exception and the last 20 steps. |
| `result.txt` in the check's folder (under `%LOCALAPPDATA%\FieldToFinish\smoke\<run>\`) | The harness's checks, plus `STEP` lines before and after each operation, the plugin's own `TRACE` lines (commands starting, finishing and failing; the point reader point by point), and `EXCEPTION` lines with the full exception and stack. Every line is written through to the disk before the next step, so it survives the process ending. |
| `<harness>.log` | The accoreconsole console, as before. |

The plugin writes `TRACE` lines only when `FTF_TRACE_FILE` names a file. The smoke test sets it
for its runs; an office session never has it, and then nothing is written.

The harnesses reach the plugin by reflection, so they name every plugin method by its exact
signature (`HarnessContractTests` checks each against the compiled plugin), check them all before
seeding, and record any exception in full before ending the run as FATAL.

## For surveyors

Survey staff install and update from the approved release folder on the server by double-clicking
**Install or Update FieldToFinish.bat** there (Civil 3D closed). It copies the release to the
computer, verifies every file, backs up the installed FTF, installs the bundle in
`%APPDATA%\Autodesk\ApplicationPlugins`, verifies it again, and puts the previous FTF back if
anything fails. Civil 3D then loads FTF by itself at every start; nobody uses NETLOAD.

Only `UPDATEFTF -PublishTo <folder>` puts a release there, and only after the whole check above,
including the style check, passed in the same run. The folder layout, the approval step, what is
kept on update, rollback and diagnostics are in [docs/network-deployment.md](docs/network-deployment.md);
the staff instructions are `deploy\setup\README.txt` (copied into the folder).
