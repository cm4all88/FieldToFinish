# Diagnostics in the FTF window

How the FTF window reports what it found in a drawing: which points are real
problems, which need a setup decision, and which are normal. Also covers the
processing record stored in the drawing, the Lines statuses, the Standards page
and the hooks other pages use to open a settings section at the right item.

## The three categories (Unknown Codes)

Every survey point is classified by `FieldCodes.Review.CodeDiagnostics` (a pure,
unit-tested class built on the parser's result):

| Category | Kind | Meaning |
|---|---|---|
| **Error** | Blank description | The point has no description. |
| | Could not parse | A rule matched, then part of the description could not be read (unrecognised modifier, a trunk or rotation that is not a number...). |
| | Invalid value | A rule matched and read a value it cannot use (missing dripline radius, unknown species letter, out-of-range value). |
| **Needs configuration** | No rule for code | The description carries data but no rule handles the code. |
| **Informational** | Civil 3D linework | A linework code (`lineworkCodes`), or a configured line feature's code: Civil 3D builds the figure, FTF labels the line. |
| | Never-draw code | On the rules file's `ignoreCodes`. |
| | Ignored by you | Marked ignored on the Unknown Codes page (see below). |
| | Bare code | Only the code was shot; the Civil 3D symbol stands as-is. |

Points that matched a rule and finish (warnings included) are *Processed* and are
not listed on Unknown Codes; warnings appear on the Reports page.

The categories partition the drawing's points:

```
Total = Eligible + Needs configuration + Skipped on purpose
Eligible = Processed + Errors
```

"Unknown codes" on the Overview is the number of distinct codes needing
configuration. A code you marked ignored is never counted as unknown.

Unknown Codes shows filter buttons with point counts (Needs attention = errors +
needs configuration, Errors, Needs configuration, Informational, All), a sortable
list grouped by code, and a detail pane with the reason, what to do, the point
numbers and real sample descriptions.

Actions:

- **Configure Rule...** (or double-click) on a code needing configuration opens
  Settings > Point Features with the code handed over (see *Navigation hooks*).
  On an error it hands over the rule the point matched instead.
- **Mark Ignored** on a code needing configuration stops reporting it.
- **Stop Ignoring** on a code ignored by you puts it back under Needs configuration.

## Ignored codes (settings)

Saved in the settings file as a new section:

```json
"diagnostics": {
  "ignoredCodes": [ "XYZ", "TEMP" ]
}
```

- Plain field codes (letters, digits, `-`, `_`), stored upper case. A trailing
  sequence number is covered, so `XYZ` also ignores `XYZ2` -- the same rule as the
  rules file's code lists.
- The parser skips an ignored code **only when no rule matches** (the codes join
  the list it consults after rule matching), so a rule added later still wins and
  a configured feature can never be silenced. A code the rules label when shot
  bare (`symbolLabels`, e.g. `PP`) is never handed to the parser, so its labels keep
  being placed.
- Ignored points are Informational ("Ignored by you") in the window and are left
  out of the unknown-codes CSV that Point Finishing writes.
- Saved to the settings file in force when that is the drawing's folder, a
  drafting profile or your own settings; otherwise (shipped plugin copy, or no file
  at all) to your own settings (`%APPDATA%\FieldToFinish\ftf-settings.json`).
- A settings file written before this section existed loads with an empty list.

Code: `FieldCodes.Settings.DiagnosticsSettings` (registered as
`FtfSettings.Diagnostics`; `FtfSettings.ApplyTo` applies it to the rules object).

## Overview and Reports

The Overview shows two columns:

- **In this drawing** -- read from the drawing now, including earlier sessions'
  work: COGO points, eligible points (processed / errors), points needing
  configuration and unknown codes, points skipped on purpose (with the reason
  breakdown), FTF-owned objects by kind (model and paper space), and the last
  processing date and outcome stored in the drawing.
- **This session** -- only what this window did since it opened: when it opened,
  processing runs, the last run's outcome, the change in FTF objects across those
  runs, and the last review scan.

It also names the settings and rules files in force.

The Reports page shows the same counts, then a sortable list of every error,
warning and point needing configuration (severity, point number, code, kind,
description, reason), filterable. Selecting a row shows the full parse detail;
with nothing selected the detail pane shows the last run stored in the drawing
(stage by stage) and this session's activity. Both CSV exports stay available.

### The processing record

After every run started from the window, a small JSON record is stored in the
drawing (Xrecord `LASTRUN` under the `FTF_FIELDTOFINISH` dictionary, beside the
selected profile name):

```json
{"version":1,"finishedUtc":"2026-10-30T21:32:00Z","source":"FTF window","user":"jdoe",
 "stages":[{"name":"FTFPOINTS","ok":true},{"name":"FTFLABELS","ok":false,"error":"..."}],
 "stoppedEarly":false,"points":120,"processed":80,"errors":3,"needsConfiguration":9,
 "skipped":28,"ownedObjects":412}
```

It is part of the drawing's undo and saves with it. An unreadable record is
ignored. `FieldCodes.Reporting.ProcessingRecord` reads and writes it;
`FtfStatusService.RecordRun` stores it. Runs from FTFRUN or single commands at the
command line are not recorded yet.

## Lines

Each line gets a status and a reason (`FieldCodes.Review.LineDiagnostics`):

| Status | Meaning |
|---|---|
| Ready to label | Identified, label text configured; the reason says what the automatic pass would place. |
| No label text | Identified, but the line feature has no label text. |
| Ambiguous | A shared layer whose candidate features label differently; FTFLABELLINE asks which. |
| Feature disabled | A candidate line feature is switched off. |
| Below minimum length | Under the automatic pass's minimum; FTFLABELLINE still labels it. |
| Not a line feature | Nothing matches its figure name, TBC name or layer. |
| TBC name ignored | Its TBC name is on `ignoreLineNames`. |

Selecting a line lists every setting that decides its label, with its current
value and where it is set: label text and alternates, label layer, text style and
height, minimum length and repeat interval (automatic pass only), alignment,
placement and offset, masking, duplicate prevention (built in), lettered
linetypes and whether the automatic pass is on. **Line Label Settings...** (or
double-click) opens Settings > Line Labels at that feature. Labelling stays
interactive: the page only starts FTFLABELLINE, it never places labels itself.

## Standards

Every row says what defines it -- `Settings`, `Rules: ...`, `Built-in`, `Civil 3D`,
`Drawing`, or `Not available yet` -- how to change it, and, where a Settings
section really changes it, opens that section on double-click. Rows marked
*Not available yet* are fixed behaviour shown for information; nothing changes
them. The page header names the settings and rules files in force.

## Navigation hooks

Set before navigating; the target page reads them when it builds or becomes
visible, then calls `Clear()`. Both raise a static `Requested` event so a page that
is already built can react.

- `FieldCodes.Cad.Setup.PointFeatureNavigation`
  - `PendingCode` -- the code to configure ("XYZ"), sequence digits removed.
  - `PendingSample` -- a real description carrying it, for the rule's test input.
  - `PendingRuleId` -- for an error, the rule the point matched.
  - `Request(code, sample, ruleId)`, `HasPending`, `Clear()`, `Requested`.
- `FieldCodes.Cad.Setup.LineLabelNavigation`
  - `PendingFeature` -- the line feature code ("FNC"); first candidate on a shared
    layer; null for a line that is not a configured feature.
  - `PendingCandidates`, `PendingFigure` ("FNC2"), `PendingLayer`.
  - `Request(feature, candidates, figure, layer)`, `HasPending`, `Clear()`, `Requested`.

## Tables

Every list on Points, Lines, Unknown Codes, Reports and Standards sorts by clicking
a column heading (again to reverse; an arrow shows the sorted column). Numbers sort
by value, codes and figure names naturally (RWC2 before RWC10), empty cells last.
Columns are resizable, and each list sits above its detail pane on a draggable
split. Helper: `FieldCodes.Cad.Ui.ListViewSorting`; ordering:
`FieldCodes.Review.NaturalOrder`.

## Verification

The classification, counting, settings round-trip, processing record, line
statuses, ordering and the Standards view are unit tested
(`tests/FieldCodes.Tests`). The window itself has only been compile-checked
(`tests/CadCompileCheck`); it has not been shown in Civil 3D.
