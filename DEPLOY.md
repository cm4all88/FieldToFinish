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
`dotnet test tests/FieldCodes.Tests` and `dotnet build tests/CadCompileCheck` (which compiles the
whole plugin against API stand-ins, so a bad merge fails before anyone builds on Windows). Then
`UPDATEFTF` picks it up.

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
| `-PublishTo U:\...\FieldToFinish-Setup` | Also publish the validated setup folder to the office copy; surveyors installed from it update when they next close Civil 3D. |
| `-FixStartup` | Disable other registry startup entries that load an FTF DLL (backed up first). |
| `-SkipSmoke` | Skip the in-Civil 3D test (the install is then unproven). |

Exit codes: `0` installed or already current, `1` failed (installed FTF unchanged or restored),
`2` validated but Civil 3D is open.

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

`tests\LiveSmoke\run-contour.ps1 -Installed` runs the same test against whatever is installed.

## For surveyors

The surveyor installer is unchanged: `deploy\make-release.ps1` builds the setup folder
(`Install FTF.bat`), and FTF installed from an office folder updates itself when Civil 3D closes
(`FTFUPDATE`). `UPDATEFTF -PublishTo <office folder>` publishes a validated approved build there.
