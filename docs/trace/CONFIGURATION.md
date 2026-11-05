# TRACE configuration: how standards are separated

## Layout
| Path | Purpose | Ships to customers |
|---|---|---|
| `config/default/rules.json` | Neutral TRACE factory configuration (tree, pole and sign examples; empty firm-specific tables) | **Yes** |
| `config/internal/parametrix/` | The internal Parametrix rules and exhibit profiles, moved here unchanged (git rename, history kept) | **No** |
| `%APPDATA%\FieldToFinish\rules.json` | Office level: a firm's own standard; survives updates | n/a (user data) |
| `rules.json` beside the drawing | Per-job override | n/a |

Resolution order is unchanged: drawing override, then office, then factory. The editor never writes the factory file.

## Edition switch
The factory file is chosen at build time. `Default` is the default so a plain build is the commercial one.

```bash
dotnet build src/FieldCodes.Cad -p:TraceEdition=Parametrix   # internal build only (Windows + Civil 3D)
```
```powershell
.\deploy\install-dev.ps1 -Edition Parametrix                 # internal dev install
.\deploy\build-release.ps1                                   # commercial bundle + release guard
```
`build-release.ps1` fails if the bundle contains Parametrix, PMX, or internal path strings.

## Bringing a firm's standards in without editing JSON
`FieldCodes.Editing.StandardsImport` takes the rules text plus a CSV and returns the new rules text and a row-by-row report (Added, Updated, Unchanged, Rejected with reason). No row is dropped silently; unknown columns and a missing `code` column are refused; the result must compile as the parser sees it.

| Table | CSV columns |
|---|---|
| Line features | `code,name,layer,label,labelLayer,placement,aliases` (placement: Left, Right, OnLine; aliases separated by `|` or `;`) |
| Symbol labels | `code,layer,label` |
| Linework codes, control codes, ignore codes | `code` |

Blank cells leave existing values alone, so a re-import only changes what it supplies. Status: core logic unit-tested; **no Civil 3D command or setup-window button calls it yet** (needs the Windows pass).

## What is still profile-independent code defaults
Layer names such as `V-TREE-TEXT` in the settings classes follow the generic National CAD Standard survey style, are editable in setup and per profile, and are not Parametrix-specific. Remaining hard-coded items to move into settings during the Windows pass: control table layer `V-CTRL-TABL` (`SpotCommands.cs`), the `TEXT` token and `-E` existing-suffix convention in `LabelLayerResolver` (only used when matching layers exist, otherwise the configured default is used).

## Excluded modules
Easement, exhibit, sheet and utility settings still exist in this repository with Parametrix-flavoured defaults. They are outside TRACE 1.0 and will not be copied to the commercial repository.
