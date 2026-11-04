# Network deployment: approved releases for survey staff

The goal: you approve a build, it goes into one folder on the server, and survey staff install
or update FTF by double-clicking one file there. Civil 3D then loads FTF by itself every time it
starts. Nobody uses NETLOAD, and no DLL ever runs from the network.

```
  your workstation                      the server                         a surveyor's workstation
  ----------------                      ----------                         ------------------------
  UPDATEFTF -PublishTo <folder>  --->   <folder>\                   --->   double-click
    build, unit tests,                    Install or Update                  "Install or Update FieldToFinish.bat"
    installer tests,                      FieldToFinish.bat                    copy locally, verify, back up,
    install, smoke test                   Bundle\ (verified)                   install, verify, register
    in Civil 3D, then publish             release.json, notes                Civil 3D loads it at every start
```

## The approved release folder

Pick any folder on the server. Nothing in FTF hard-codes a location: you name it when you publish,
and every copy installed from it remembers it. Example (use your own):

```
\\<server>\<share>\CAD\FieldToFinish\             <- the approved release: staff read, the publisher writes
    Install or Update FieldToFinish.bat           what staff double-click
    Install FTF.bat                               the older name; does the same
    Restore previous FieldToFinish.bat            puts back the FTF before the last update
    Check FTF.bat / Repair FTF.bat                diagnostics / clear Civil 3D's stale record
    Uninstall FTF.bat
    install.ps1, check.ps1, ftf-common.ps1        the work behind the batch files
    README.txt                                    instructions for staff
    RELEASE-NOTES.txt                             what changed, who approved it, when
    release.json                                  commit, channel, approved by / when / on which computer,
                                                  the checks it passed, SHA-256 of every setup file
    version.txt                                   one line: date, branch, commit
    updates-from.txt                              the folder itself, so installed copies update from here
    Bundle\                                       the Autodesk ApplicationPlugins bundle
        PackageContents.xml
        build-info.json                           commit, channel, built, and SHA-256 of every bundle file
        version.txt
        Contents\2024\                            FieldCodes.Cad.dll, FieldCodes.dll, dependencies,
                                                  factory rules.json, compare-profiles, ribbon resources
\\<server>\<share>\CAD\FieldToFinish.previous-<time>\   the three releases before it, kept by publishing
```

Permissions:

- Survey staff: read (and read and execute) on the folder.
- Whoever publishes: modify on the folder's parent, because publishing writes `<folder>.incoming`
  and renames.

Staff never need to write there: installing writes only to their own profile.

## Publishing an approved release (the approval step)

On your workstation, with Civil 3D closed:

```
C:\dev\UPDATEFTF -PublishTo "\\<server>\<share>\CAD\FieldToFinish" -ReleaseNotes "Point accountability fixes; style check required"
```

UPDATEFTF then:

1. Builds the approved branch (`origin/integration`) in its own checkout, stamped **approved**.
2. Runs the unit tests (the regression suite).
3. Runs the installer tests:
   - `deploy\test-release.ps1` on the real package;
   - `deploy\test-installer.ps1` on every kind of release the installer must handle or refuse.
4. Installs the build on your workstation.
5. Runs the smoke test in Civil 3D: the main checks plus the required identity, point
   accountability and style checks. Any failure puts your previous FTF back, and nothing is published.
6. Publishes (`deploy\publish-release.ps1`):
   - checks the package again: complete, every file's SHA-256, every FTF DLL the same build, approved;
   - copies it to `<folder>.incoming` beside the release folder and verifies the copy on the server;
   - only then swaps it in, keeping the release it replaces as `<folder>.previous-<time>`;
   - writes `RELEASE-NOTES.txt`: your notes, then the commits since the release it replaces;
   - writes `release.json`: who approved it, when, on which computer, and which checks it passed.

Publishing always comes right after a passing smoke test in the same run. UPDATEFTF installs and
smoke-tests again even when the build is already installed.

Never published:

- a `-Dev` build;
- a `-SkipSmoke` run;
- a build with uncommitted changes;
- a package that does not verify.

`publish-release.ps1` refuses each of these on its own too.

"Approved" means: built from `origin/integration` and passed every check above. Putting it on the
server is a separate, deliberate act: only `-PublishTo` does it. A development build you are trying
out (`UPDATEFTF -Dev -Ref ...`) stays on your workstation.

## Installing and updating (survey staff)

1. Save drawings and close Civil 3D.
2. Double-click **Install or Update FieldToFinish.bat** in the approved release folder.
3. Start Civil 3D. Type `FTFVERSION` to see the build.

The same file does the first install, every update, repairs, and "up to date". `README.txt` in the
folder is the staff instructions.

### What the double-click does

| Step | |
|---|---|
| 1 | Stops at once if Civil 3D is running, with "close it and run this again". |
| 2 | Copies the whole release to `%TEMP%\FTF-Setup` (a network drop mid-copy changes nothing). |
| 3 | Verifies the copy: every file against `Bundle\build-info.json` (SHA-256), nothing extra, `FieldCodes.Cad.dll` and `FieldCodes.dll` both stamped with the release's commit, and an approved release (a development build is refused). |
| 4 | Reads the installed build (`build-info.json` in the installed bundle) and compares. |
| 5 | Decides and says so: **install** (none yet), **update** `old -> new` (noting a release older than the installed build, or one replacing a development build), **repair** (same release, damaged files), or **up to date** (nothing is copied, nothing is backed up). |
| 6 | Backs up the installed FTF to `%LOCALAPPDATA%\FieldToFinish\backups` (the newest backup of each of the last three builds). |
| 7 | Copies the release beside the installed bundle (`FieldToFinish.bundle.new`), verifies it again, and carries over what the office or the person saved inside the old bundle. Then it swaps the two folders by rename. |
| 8 | Verifies the installed files where Civil 3D loads them, and writes Civil 3D's per-user startup record. If either fails, the previous FTF and its startup record are put back at once, and the failed copy is kept in `%LOCALAPPDATA%\FieldToFinish\failed`. Never a partly installed bundle. |
| 9 | Finds other copies of FTF: another bundle, an old startup entry (for example one pointing at a network DLL), the Startup Suite, an `acad*.lsp` NETLOAD. It switches off the per-user ones (backed up first) and names the rest. |
| 10 | Says what is installed, what it was, where updates come from, and the log file. |

Every run writes `%LOCALAPPDATA%\FieldToFinish\logs\install-<time>.log`.

Updates also arrive without anyone doing anything: FTF installed from the folder compares itself
with it when Civil 3D starts and, if a newer release is there, installs it when Civil 3D closes
(`FTFUPDATE` shows what it found). That path runs the same `install.ps1`, so it verifies,
backs up and rolls back the same way. It only ever moves forward.

### Automatic loading

FTF is an Autodesk ApplicationPlugins bundle in `%APPDATA%\Autodesk\ApplicationPlugins`. Civil 3D
discovers it at every start through `PackageContents.xml`. The installer also writes Civil 3D's
per-user startup record (`Applications\FieldToFinish`, `LOADCTRLS=2`) pointing at the installed
`FieldCodes.Cad.dll`, for machines that do not scan the folder. The DLLs run from the local
profile.

No admin rights are needed.

## What belongs to a release, and what is kept

| Where | What | On install or update |
|---|---|---|
| `%APPDATA%\Autodesk\ApplicationPlugins\FieldToFinish.bundle` | The program: DLLs, `PackageContents.xml`, factory `rules.json`, factory compare profiles, ribbon resources, `build-info.json`, `version.txt`, `source.txt` (where updates come from) | Replaced as a whole. |
| ... `Contents\2024\ftf-settings.json` (+ `.bak`) | The **office** settings level, saved from Settings inside the bundle | Carried into the new bundle. |
| ... any other file FTF or the person wrote inside the bundle | Not part of the release | Carried over (never a program file). |
| `%APPDATA%\FieldToFinish` | Office rules (`rules.json`), personal settings (`ftf-settings.json`), saved profiles, layer mappings, text and leader styles, feature rules, custom commands, workflow templates | Never touched. |
| Beside each drawing | Project settings (`ftf-settings.json`, `rules.json`), reports | Never touched. |
| `%LOCALAPPDATA%\FieldToFinish` | Logs, backups, failed copies, the local tools | Written by the installer. |

## Rollback and recovery

| Situation | What to do |
|---|---|
| An update fails while installing | Nothing: the installer puts the previous FTF back by itself and says so. |
| The new release misbehaves on one computer | Close Civil 3D, double-click **Restore previous FieldToFinish.bat** (in the release folder, or `%LOCALAPPDATA%\FieldToFinish` when the network is down). It puts back the newest intact backup of the build before. Settings stay as they are now. |
| The new release misbehaves everywhere | On the server, rename the release folder aside, rename `<folder>.previous-<time>` back to the folder name, and tell staff to double-click the batch file. It installs whatever the folder holds and says "OLDER". The automatic update never goes backwards on its own. Or publish the earlier approved build again with UPDATEFTF. |
| Your own workstation | `UPDATEFTF -Rollback` puts back UPDATEFTF's last backup. |
| Civil 3D does not show FTF | **Repair FTF.bat** clears a stale Civil 3D record. **Check FTF.bat** writes `ftf-check.txt` (desktop, and a copy in the logs). |
| Remove FTF | **Uninstall FTF.bat**: backs up, removes the bundle and the startup record, keeps settings. |

## Diagnostics

- `FTFVERSION` in Civil 3D:
  - the loaded build and the DLL it was loaded from;
  - the installed build;
  - a warning when FTF was loaded from outside the installed bundle.
- **Check FTF.bat** (`check.ps1`) writes a report:
  - the Autodesk releases on the computer;
  - the installed build, and whether every file is the one it shipped;
  - the approved release folder: reachable or not, which build is there, how it compares;
  - other copies of FTF;
  - files marked "from another computer";
  - `APPAUTOLOAD`;
  - Civil 3D's record for FTF;
  - the last install log;
  - how many backups there are.
- `%LOCALAPPDATA%\FieldToFinish\logs`: every install, update, restore and check.

## Moving a workstation off NETLOAD

Copies of FTF loaded from files on the server caused the DLL mismatches. The first double-click
of the batch file:

- switches off a per-user Civil 3D startup entry that loads `FieldCodes*.dll` from anywhere other
  than the installed bundle (its values saved in `%LOCALAPPDATA%\FieldToFinish\backups\startup-<time>`);
- renames a second per-user FTF bundle to `*.disabled-by-FTF-<time>`;
- names, for removal by hand, a Startup Suite entry (APPLOAD > Startup Suite > Contents) or an
  `acad*.lsp` NETLOAD line, and machine-wide entries (IT).

A NETLOAD typed by hand loads only for that session; just stop doing it.

## Tests

`deploy\test-installer.ps1` runs on Windows PowerShell 5.1 and PowerShell 7, in scratch folders. It
builds stand-in FTF assemblies stamped like the real build and packages them as `make-release.ps1`
does. UPDATEFTF runs it before every install, and so does `tools/check-before-push.sh` where
PowerShell is installed.

What it covers:

- a clean install, an update, "no update needed" (nothing rewritten), a repair, and a check;
- settings: inside the bundle, and in APPDATA;
- a release older than the installed one;
- duplicate bundles;
- releases that are damaged, missing a file, carrying an extra file, without `build-info.json`,
  or with DLLs from another build;
- development builds;
- Civil 3D open;
- rollback after the swap;
- restore previous, and uninstall;
- the logs, the local tools and the diagnostic report;
- publishing an approved release, and refusing development or inconsistent ones.

`deploy\test-release.ps1` (Windows) checks the real package and the registry writes.

| What | Status |
|---|---|
| `test-installer.ps1` | 83 PASS, 0 FAIL on PowerShell 7.4 (Linux), 2026-11-04. Civil 3D's startup record (registry) is Windows-only and was not run there. |
| `test-release.ps1`, and the whole flow in UPDATEFTF | Updated for the new installer. Runs on Windows in UPDATEFTF; **not yet run**. |
| A real Civil 3D workstation (the publisher's, and the first surveyor's) | **Not tested.** Nothing here claims a surveyor's computer works until the batch file has been run there. |
