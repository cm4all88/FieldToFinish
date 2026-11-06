# TRACE interface: what you should see (first Civil 3D check)

Built from the approved design. Compiled and type-checked against stand-ins for the Autodesk types only: **not yet run in Civil 3D**, so this is the first real check.

## On startup (no typing)
1. A **TRACE** tab on the ribbon, with panels: TRACE (Run TRACE, Palette), Cleanup, Labels, Trees, Linework, Drafting, Tables, Setup. No FTF tab.
2. The **TRACE palette** docked on the right (first run only: afterwards it reopens only if you left it open). Navy header with the logo, a blue **Run TRACE** button, a list of steps with ticks, then the tool tiles grouped as on the ribbon.

## Quick checks
| Do | Expect |
|---|---|
| Hover a ribbon button or palette tile | A plain-English tip, no `FTF` name |
| Untick a step, close and reopen Civil 3D | The tick stays off |
| Untick every step | Run TRACE greys out with "Tick at least one step to run." |
| Click **Run TRACE** | The command line shows "TRACE: running N step(s)" and step names such as "Point labels"; finishes with "TRACE finished" |
| Move a label by hand, Run TRACE again | Your label stays where you put it; no duplicates |
| Click each ribbon button once on a copy of a drawing | It runs the same tool the old FTF command ran |
| **Setup > Profiles & Standards** | The settings window opens titled TRACE; no Sheets, Dips, Easements or Exhibits sections |
| Drag the palette edge, collapse it with the auto-hide button, undock it, close and reopen Civil 3D | Size, side and position are remembered |
| Press **Palette** on the ribbon | Palette hides, then shows |
| Type `TRACE` | Same as the Palette button |

## Known limits of this first version
- Messages from the existing tools are shown in TRACE wording (for example "Driplines: 16 arc(s)...") in the TRACE edition; the tools' own text is unchanged and typing an old `FTF...` command still works, by design.
- The Settings window has had a first branding pass (navy navigation, blue actions, green marker, Segoe UI, TRACE wording). The full Profiles and Standards redesign from the mockups is a later stage.
- The Profiles and Standards window, Review Queue, Start window and About are not built yet (still the existing settings window). The Reports and Help buttons are left out because no existing function backs them.
- Icons follow the Civil 3D colour theme at startup (dark by default; a theme change needs a restart).
- Tool tiles show an icon over the name; spacing may need a pass once seen at your screen scaling.

## If something fails
Send: a screenshot, the command-line text (press F2, copy), and `%APPDATA%\TRACE\trace-ui.json` if the palette misbehaves.
