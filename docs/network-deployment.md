# Network deployment: approved releases for survey staff

The goal: you approve a build, it goes into one folder on the server, and survey staff install
or update FTF by double-clicking one file there. Civil 3D then loads FTF by itself every time it
starts. Nobody uses NETLOAD, and no DLL ever runs from the network.

```
  your workstation                       the approved release folder             a surveyor's workstation
  ----------------                       ---------------------------             ------------------------
  UPDATEFTF -Publish              --->   Install or Update FieldToFinish.bat --->  double-click it
    folder check, build, unit tests,      Bundle\ (verified)                         copy locally, verify, back up,
    installer tests, install,             release.json, notes                        install, verify, register
    smoke test in Civil 3D, publish       Previous releases\                         Civil 3D loads it at every start
```

## The official folder

The office's approved release folder is configured with the scripts, in
`deploy\release-folder.txt` (by its drive path, as the office knows it). Check it, on the
publishing workstation, before the first publish:

```
C:\dev\UPDATEFTF -CheckReleaseFolder
```

That runs `deploy\check-release-folder.ps1`. `UPDATEFTF -Publish` runs it again before anything is
built or written there.

| Check | |
|---|---|
| Where it really is | The drive path (`U:\...`) is turned into the `\\server\share\...` path behind it, on the publishing workstation. Every installed copy records that path, so it finds updates whether or not the drive letter is mapped there, or mapped differently. The server's name is never written into the repository. A drive letter that is not mapped in that PowerShell stops the publish. |
| Reachable | The folder, or its parent if it does not exist yet (publishing creates it). |
| What it holds | The approved release there (or an older setup folder, or nothing), how many previous releases are kept, and any other files (publishing leaves them alone). |
| You can publish there | A write test: create a folder, write a file, rename both, delete both, exactly as publishing does. Nothing is left behind. |
| Who can read and change it | The folder's permissions: who can change it (a warning if a general group such as Domain Users or Everyone can), and who can read it (a warning if no general group can, so every surveyor must be in a listed group). |

It changes nothing on the share. Run it as the person who publishes, **without** "Run as
administrator" (an elevated PowerShell does not see mapped drives). Each check is logged in
`%LOCALAPPDATA%\FieldToFinish\logs`. `-PublishTo <folder>` checks or publishes another folder
instead; `UPDATEFTF -Status` names the configured one.

What it cannot see from a workstation: the share permissions on the server (as opposed to the
folder's), and what another person can actually do. Confirm those with IT, and have one surveyor
run **Check FTF.bat** after the first install (it reports whether the release folder is reachable).

Permissions to ask IT for, on the release folder itself:

- Survey staff: Read & execute.
- Whoever publishes: Modify.

Publishing never replaces or renames the folder itself, so these stay as IT sets them.

## What the folder holds

```
<the approved release folder>\
    Install or Update FieldToFinish.bat           what staff double-click
    Install FTF.bat                               the older name; does the same
    Restore previous FieldToFinish.bat            puts back the FTF this computer had before its last update
    Check FTF.bat / Repair FTF.bat                diagnostics / clear Civil 3D's stale record
    Uninstall FTF.bat
    install.ps1, check.ps1, ftf-common.ps1        the work behind the batch files
    README.txt                                    instructions for staff
    RELEASE-NOTES.txt                             what changed, who approved it, when
    release.json                                  commit, channel, approved by / when / on which computer,
                                                  the folder's \\server\share path, the checks it passed,
                                                  SHA-256 of every setup file
    version.txt                                   one line: date, branch, commit
    updates-from.txt                              the folder's \\server\share path, so installed copies update from here
    Bundle\                                       the Autodesk ApplicationPlugins bundle
        PackageContents.xml
        build-info.json                           commit, channel, built, and SHA-256 of every bundle file
        version.txt
        Contents\2024\                            FieldCodes.Cad.dll, FieldCodes.dll, dependencies,
                                                  factory rules.json, compare-profiles, ribbon resources
    Previous releases\<time>-<commit>\            the three releases it replaced, each whole
    _publishing\                                  only while a release is being published (hidden)
```

The batch file copies only the release: not `Previous releases`, not `_publishing`.

## Publishing an approved release (the approval step)

On your workstation, with Civil 3D closed:

```
C:\dev\UPDATEFTF -Publish -ReleaseNotes "Point accountability fixes; style check required"
```

(`-PublishTo <folder>` publishes to another folder instead of the office's.) UPDATEFTF then:

1. Checks the release folder again (reachable, write test). Nothing is built if it fails.
2. Builds the approved branch (`origin/integration`) in its own checkout, stamped **approved**.
3. Runs the unit tests (the regression suite).
4. Runs the installer tests:
   - `deploy\test-release.ps1` on the real package;
   - `deploy\test-installer.ps1` on every kind of release the installer must handle or refuse,
     and on publishing.
5. Installs the build on your workstation.
6. Runs the smoke test in Civil 3D: the main checks plus the required identity, point
   accountability and style checks. Any failure puts your previous FTF back, and nothing is published.
7. Publishes (`deploy\publish-release.ps1`):
   - checks the package again: complete, every file's SHA-256, every FTF DLL the same build, approved;
   - copies it into the folder's `_publishing` and verifies the copy there, on the server;
   - moves the current release's files into `Previous releases\<time>-<commit>` and the new ones
     up (renames on the share: a moment). If any of that fails, everything is moved back;
   - verifies the release again in place; if it does not verify, the previous one is put back;
   - keeps the three most recent previous releases;
   - writes `RELEASE-NOTES.txt` (your notes, then the commits since the release it replaces) and
     `release.json` (who approved it, when, on which computer, which checks it passed).

Publishing always comes right after a passing smoke test in the same run. UPDATEFTF installs and
smoke-tests again even when the build is already installed. When it was asked to publish and did
not, it exits with code 3 and says why; the release folder is then unchanged.

Never published:

- a `-Dev` build;
- a `-SkipSmoke` run;
- a build with uncommitted changes;
- a package that does not verify.

UPDATEFTF refuses the first two before building anything, and `publish-release.ps1` refuses each
of these on its own too.

"Approved" means: built from `origin/integration` and passed every check above. Putting it on the
server is a separate, deliberate act: only `-Publish` (or `-PublishTo`) does it. A development
build you are trying out (`UPDATEFTF -Dev -Ref ...`) stays on your workstation.

A surveyor who double-clicks during the moment the files are switched gets "not a complete
release" and nothing changes on their computer. Publishing stops, and puts everything back, if a
file in the release is open; for example when someone has FTF loaded into Civil 3D from that
folder with NETLOAD.

## Installing and updating (survey staff)

1. Save drawings and close Civil 3D.
2. Double-click **Install or Update FieldToFinish.bat** in the approved release folder.
3. Start Civil 3D. Type `FTFVERSION` to see the build.

The same file does the first install, every update, repairs, and "up to date". `README.txt` in the
folder is the staff instructions. It works from the mapped drive or from the `\\server\share` path
(opened that way, Windows first prints "UNC paths are not supported"; that is harmless).

### What the double-click does

| Step | |
|---|---|
| 1 | Stops at once if Civil 3D is running, with "close it and run this again". |
| 2 | Copies the release to `%TEMP%\FTF-Setup` (a network drop mid-copy changes nothing). |
| 3 | Verifies the copy: every file against `Bundle\build-info.json` (SHA-256), nothing extra, `FieldCodes.Cad.dll` and `FieldCodes.dll` both stamped with the release's commit, and an approved release (a development build is refused). |
| 4 | Reads the installed build (`build-info.json` in the installed bundle) and compares. |
| 5 | Decides and says so: **install** (none yet), **update** `old -> new` (noting a release older than the installed build, or one replacing a development build), **repair** (same release, damaged files), or **up to date** (nothing is copied, nothing is backed up). |
| 6 | Backs up the installed FTF to `%LOCALAPPDATA%\FieldToFinish\backups` (the newest backup of each of the last three builds). |
| 7 | Copies the release beside the installed bundle (`FieldToFinish.bundle.new`), verifies it again, and carries over what the office or the person saved inside the old bundle. Then it swaps the two folders by rename. |
| 8 | Verifies the installed files where Civil 3D loads them, and writes Civil 3D's per-user startup record. If either fails, the previous FTF and its startup record are put back at once, and the failed copy is kept in `%LOCALAPPDATA%\FieldToFinish\failed`. Never a partly installed bundle. |
| 9 | Finds other copies of FTF: another bundle, an old startup entry (for example one pointing at a network DLL), the Startup Suite, an `acad*.lsp` NETLOAD. It switches off the per-user ones (backed up first) and names the rest. |
| 10 | Records where updates come from (the folder's `\\server\share` path) and says what is installed, what it was, and the log file. |

Every run writes `%LOCALAPPDATA%\FieldToFinish\logs\install-<time>.log`.

Updates also arrive without anyone doing anything: FTF installed from the folder compares itself
with it when Civil 3D starts and, if a newer release is there, installs it when Civil 3D closes
(`FTFUPDATE` shows what it found). That path runs the same `install.ps1`, so it verifies,
backs up and rolls back the same way. It only ever moves forward.

A copy installed earlier that recorded the folder by its drive letter keeps working where the
drive is mapped; its next install or update records the `\\server\share` path (**Check FTF.bat**
notes which it has).

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
| A publish fails part way | Nothing: the release folder is put back as it was, and UPDATEFTF says why (exit code 3). |
| The new release misbehaves on one computer | Close Civil 3D, double-click **Restore previous FieldToFinish.bat** (in the release folder, or `%LOCALAPPDATA%\FieldToFinish` when the network is down). It puts back the newest intact backup of the build before. Settings stay as they are now. |
| The new release misbehaves everywhere | `UPDATEFTF -RestoreRelease` puts the release the folder had before back (the newest one in `Previous releases` that verifies; the current one goes into `Previous releases`). Staff then double-click the batch file, and it says "OLDER". The automatic update never goes backwards on its own. Or publish the earlier approved build again with UPDATEFTF. |
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
- `UPDATEFTF -CheckReleaseFolder` on the publishing workstation: the release folder check above.
- `%LOCALAPPDATA%\FieldToFinish\logs`: every install, update, restore, check and release folder check.

## Moving a workstation off NETLOAD

Copies of FTF loaded from files on the server caused the DLL mismatches. The first double-click
of the batch file:

- switches off a per-user Civil 3D startup entry that loads `FieldCodes*.dll` from anywhere other
  than the installed bundle (its values saved in `%LOCALAPPDATA%\FieldToFinish\backups\startup-<time>`);
- renames a second per-user FTF bundle to `*.disabled-by-FTF-<time>`;
- names, for removal by hand, a Startup Suite entry (APPLOAD > Startup Suite > Contents) or an
  `acad*.lsp` NETLOAD line, and machine-wide entries (IT).

A NETLOAD typed by hand loads only for that session; just stop doing it.

If the folder already holds an older setup folder (from before release.json), the first publish
moves it whole into `Previous releases`. Copies installed from it update the same way: they
double-click, or their FTF installs the new release when Civil 3D closes.

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
- development builds, and developer installs;
- Civil 3D open;
- rollback after the swap;
- restore previous, and uninstall;
- the logs, the local tools and the diagnostic report;
- the `\\server\share` path behind a mapped drive letter;
- the release folder check: a new folder, an existing release, an unreachable folder, nothing left behind;
- publishing an approved release, and refusing development or inconsistent ones;
- publishing inside the folder: the releases it replaced kept in `Previous releases` (three),
  other files left alone, a publish that fails part way or does not verify in place undone, two
  publishes never at once;
- putting a previous release back on the server, skipping one that does not verify.

`deploy\test-release.ps1` (Windows) checks the real package and the registry writes.

| What | Status |
|---|---|
| `test-installer.ps1` | 114 PASS, 0 FAIL on PowerShell 7.4 (Linux), 2026-11-04. Civil 3D's startup record (registry) is Windows-only and was not run there. Not yet run on Windows. |
| Release folder check refusing a folder the publisher cannot write | Run by hand as an unprivileged user on Linux: refused, nothing left behind. Not part of the suite (it needs a non-administrator account). |
| Resolving a real mapped drive, reading real folder permissions | Windows only; **not yet run**. The joining of drive and path is tested with a stand-in drive map. |
| `test-release.ps1`, and the whole flow in UPDATEFTF | First run on Windows in an UPDATEFTF dev run (2026-11-04): 77 PASS, 1 FAIL, and UPDATEFTF stopped before installing. The failing check expected a drive-letter home to be recorded as written, but U: is a mapped network drive on that computer, so the installer correctly recorded the network path behind it. The check now covers a `\\server\share` home, a drive that is not mapped, and a mapped one. **Not yet re-run.** |
| The office's release folder | **Not checked.** Run `UPDATEFTF -CheckReleaseFolder` on the publishing workstation. |
| A real Civil 3D workstation (the publisher's, and the first surveyor's) | **Not tested.** Nothing here claims a surveyor's computer works until the batch file has been run there. |
