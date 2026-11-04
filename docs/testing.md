# Testing FTF: what to run, and when

Testing time goes where it finds real drafting problems. A change is tested where it could break
something; what was already verified, and is not touched, stays verified. Every check still exists
and still runs in full for milestones and for every approved release survey staff install.

## The policy

| When | What runs |
|---|---|
| **A development iteration** (a fix or a feature in progress) | The compile checks and the lints, always. The unit tests for the areas the change touches, chosen by `tools/test-plan.ps1` from `tests/areas.json`. The installer tests only when the installer changed. No Civil 3D smoke test, unless the change is something only Civil 3D can show (`UPDATEFTF -Dev -Quick -Smoke`). |
| **A change to shared code** | The full unit suite. Shared areas are marked in the map: the shared geometry, the field-code rules and the settings model, the plugin core (CadUtil, ownership stamps, FtfSession, the drawing store, build info) and the build files. A changed file that belongs to no area counts as shared. `test-plan` says when this applies and which file caused it. |
| **A milestone** (work ready to go into `integration`, or a set of changes worth proving together) | The full check: `tools/check-before-push.sh` without `--quick`, and `UPDATEFTF` without `-Quick` (the full unit suite, both installer tests, the complete smoke test in Civil 3D). Also run, by hand, the Civil 3D runners `test-plan` lists for the areas the work touched (`civil3dManual` in the map). Then record what passed (below). |
| **An approved release** (`UPDATEFTF`, `UPDATEFTF -Publish`) | Unchanged: the clean build of `origin/integration`, the full unit suite, both installer tests and every section of the smoke test in Civil 3D, all required. `-Quick` is refused for it. |

Never weaken a test, delete a failing one, or record a check as passed that did not pass in full.
A targeted run is a choice of which tests to run, never a reason to accept a failure.

## Choosing the tests: `tools/test-plan.ps1`

```
pwsh tools/test-plan.ps1                          # what changed since the branch's upstream (else HEAD~1)
pwsh tools/test-plan.ps1 -Since origin/integration
pwsh tools/test-plan.ps1 -Since origin/integration -Run
pwsh tools/test-plan.ps1 -Status
```

(On Windows without PowerShell 7: `powershell -ExecutionPolicy Bypass -File tools\test-plan.ps1 ...`.)

It lists the changed files (commits since `-Since`, plus staged, unstaged and new files; with
`-Until <commit>`, the difference between two commits) and, for them:

- the **areas** they belong to, and the files that need **no tests** (documentation, old checkpoints);
- the **unit tests**: the test classes of those areas plus a few cheap repository-wide checks, or
  **FULL SUITE** with the file that made it so;
- the **Civil 3D** smoke-test sections and hand-run runners a milestone run of this change should
  look at (the smoke test itself always runs every section);
- whether the **installer tests** (`deploy/test-release.ps1`, `deploy/test-installer.ps1`) and the
  **harness compile check** (`tests/SmokeHarnessCompileCheck`) are needed.

`-Run` runs those unit tests (`dotnet test tests/FieldCodes.Tests --filter ...`) and exits with
`dotnet test`'s exit code. `-Json` prints the plan as JSON; `-OutFile <file>` also writes it there.
`-AndFilter <filter>` adds a condition every test must meet when running (check-before-push uses it
on Linux for the four tests that compare Windows paths).

`-Status` prints the verification record: for each area, the commit, date and place its unit tests,
its Civil 3D sections and (for deployment) its installer tests last passed, **NOT VERIFIED** where
nothing is recorded, and **NEEDS REVALIDATION** where files of the area, or shared files, changed
since that commit (`git diff <commit>..HEAD`; uncommitted changes are not counted).

## Before a push: `tools/check-before-push.sh`

| Check | `--quick` | full (no option) |
|---|---|---|
| Namespace and compile-coverage lint | yes | yes |
| Compile checks: `tests/CadCompileCheck`, `tests/RealApiCompileCheck`, `tests/SmokeHarnessCompileCheck` | yes | yes |
| Unit tests | the ones `test-plan` chooses (the whole suite if no `pwsh` or `powershell.exe` is found to choose them) | the whole suite |
| Unit tests again on a Windows-style (CRLF) checkout | no | yes (off Windows) |
| PowerShell lint (Windows PowerShell 5.1 syntax) | yes | yes |
| Staff installer and publishing test | only when the installer changed | yes |

`--quick --since <commit>` compares with that commit instead of the branch's upstream. With
`git config core.hooksPath tools/githooks`, every push runs the quick check, compared with what the
remote branch already has (a new branch: with what it adds to `integration`). For a milestone, run
the full check: `FTF_FULL_CHECK=1 git push ...`, or `tools/check-before-push.sh` by itself.

## Installing a development build quickly: `UPDATEFTF -Dev -Quick`

```
C:\dev\UPDATEFTF -Dev -Ref origin/claude/my-fix -Quick
C:\dev\UPDATEFTF -Dev -Ref origin/claude/my-fix -Quick -Smoke
```

| Step | `-Dev -Quick` | `-Dev` and approved runs |
|---|---|---|
| Release build in the build checkout, the DLL stamped with the commit | yes | yes |
| Unit tests | `test-plan` (this commit's own) for what changed since the installed build's commit; the whole suite when nothing is installed, the installed build's commit is unknown or was built from uncommitted changes, or the change touches shared code | the whole suite |
| Packaging (refuses a DLL of any other commit) | yes | yes |
| Installer tests | only when the installer changed since the installed build | yes |
| Back up the installed FTF and its startup entry, install with `install.ps1` (its own verification and rollback), check every file's SHA-256, restore the backup on a failure | yes, exactly the same | yes |
| Smoke test in Civil 3D, restoring the previous FTF if it fails | only with `-Smoke` | yes (unless `-SkipSmoke`) |
| Publishing to the release folder | refused | `-Publish` / `-PublishTo` on approved runs |

A quick run says, at the start and at the end, what it did not run. Its validation is kept in
`%LOCALAPPDATA%\FieldToFinish\updates\<commit>-dev\validated.json` with `"mode": "quick"`, the commit it
compared with (`since`) and the `skipped` list; a run without `-Quick` never reuses it and validates
again in full. `-Quick` needs `-Dev`; with `-Publish` or `-PublishTo` it is refused before anything
is built.

**The first time.** `C:\dev\UPDATEFTF` runs the copy of `UPDATEFTF.ps1` in the build checkout, which
is the one from the last commit UPDATEFTF built. If that copy is older than `-Quick`, PowerShell
stops at once with *A parameter cannot be found that matches parameter name 'Quick'* (nothing is
built or changed): the older script cannot hand over to a newer one before PowerShell reads its
parameters. Run UPDATEFTF once without `-Quick` (`UPDATEFTF -Dev -Ref <a commit that has it>`, or a
plain `UPDATEFTF` once `integration` has it); from then on `-Quick` works. A quick run of a commit
older than `tools/test-plan.ps1` finds nothing to choose the tests with and tests in full; it leaves
that older UPDATEFTF in the build checkout, so the run after it needs to be a run without `-Quick`
again.

## The map and the verification record: `tests/areas.json`

Each area has an `id`, a `title`, its `paths` (repo-relative globs: `*` within a folder, `**` across
folders), `shared`, its `unitTests` (`dotnet test --filter` clauses, one per test class), the
smoke-test sections that check it (`civil3d`), any Civil 3D runners run by hand (`civil3dManual`),
and `verified`:

```json
"verified": {
  "unit":    { "commit": "<full commit>", "date": "2026-11-04", "where": "Linux, .NET 8: the full unit suite" },
  "civil3d": { "commit": "<full commit>", "date": "2026-11-04", "where": "office workstation, Civil 3D 2024, deploy/smoke-test.ps1",
               "passed": ["accountability"], "results": "accountability 104 PASS" },
  "notes": "what is known that the fields cannot say: a section that failed, a test that did not run there"
}
```

`null` means never verified. The deployment area also has `installer`: one record per installer
test that passed (`script`, `commit`, `date`, `where`, `result`).

**Recording a result.** After a check passes in full, put the full commit (`git rev-parse HEAD`), the
date, where it ran and the result into every area it covers, and commit the change:

- the full unit suite: `verified.unit` of every area; a targeted run: only the areas it ran for;
- the smoke test: `verified.civil3d` of each area whose sections all passed, with `passed` and
  `results`. A section with any FAIL is not recorded as passed: say so in `notes` instead;
- an installer test: `verified.installer` of the deployment area.

Editing the record never makes an area need revalidation. `-Status` then shows what is current.

**Keeping the map honest.** `AreaMapTests` (it runs with every change) fails when a source or test
file is in no area, a filter names a test class that does not exist, a path matches no file, or a
test that reaches an area's code (through the types it uses, the core code those use, or by reading
the area's files) is missing from that area's `unitTests`. Its message says what to add. When a new
file or test turns up there, put it in the area it belongs to; when a file is used everywhere, make
it shared rather than listing every test under it.
