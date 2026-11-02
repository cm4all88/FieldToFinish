# Dip field notes PDF

The Dip Builder can save its completed storm / sanitary dip observations as a PDF laid out like survey
field-book notes: a permanent record of what the crew measured and what was calculated from it.

- **Dip Builder** (`FTFDIP`): **Save Field Notes PDF...** on the **Map** and **Review** tabs.
- **Command line**: `FTFDIPPDF` asks for the header values (Enter keeps the one shown, `.` clears it) and saves every
  complete observation.
- **Automatically**: with *Save the field notes automatically when a structure is finished* on (Settings >
  Storm & Sewer Dips > Field notes PDF), placing a structure's label saves the PDF again.

Nothing in Civil 3D is needed to make the file: the PDF is written directly by FieldToFinish (no printer driver, no
third-party library). It uses only the standard PDF fonts (Helvetica, Helvetica-Bold, Helvetica-Oblique, Courier),
which every PDF reader has.

## What a page looks like

US Letter, portrait. Every page carries the title, the project number and name, the date of observation and the crew
at the top, and the project, the issue date / revision and **Page x of n** at the foot.

The first page adds the project block (project, number, date of observation, crew, drawing, issued), a short
"how to read these notes" key, the structures in the record, and any observations **not included** because they are
incomplete -- each with what it is missing.

Then one block per structure, kept on one page where it fits (a structure taller than a page continues with its
heading and the table head repeated):

- **Heading**: structure label (`SSMH 1025`), its type and utility, the point number.
- **Facts** (left): utility, structure type and code (and the field code if the drafter changed the type), the point
  description, rim elevation (surveyed point), position, inside size and where it came from, bottom (MD and BOT
  elevation), water (MD and WL elevation, or DRY), conditions, and the pipe configuration (how many in / out).
- **Plan sketch** (right): see below.
- **Pipe table**: one row per pipe, P1, P2... in the order they were entered -- flow (IN / OUT), size, material,
  direction as recorded, **MD**, what the dip was measured to, the **calculated elevation**, and what it connects to.
  Under each row: the reference basis (stated in the note, office convention, confirmed or entered by the drafter),
  why no invert is given when the dip was not to the invert, conditions, notes, values carried over from the other
  end (marked with a dagger), and the calculated slope to the connected structure.
- **Field note as recorded**: the typed note, verbatim, in Courier.
- **Notes** and **Warnings and qualifications**: the structure's notes, and every finding the Dip Builder's QC makes
  for it (slope, cover, reverse flow, size / material / direction mismatches, unresolved or questionable connections,
  submerged / silted / blocked pipes...), plus any office note for the field.

## Black and red

| Colour | What |
| --- | --- |
| **Black** | Everything recorded or surveyed: structure information, descriptions, sizes, materials, directions, **measured depths (MD)**, conditions and notes. |
| **Red, bold** | **INV** -- the calculated invert elevation, rim elevation minus MD. |
| **Red** | Other calculated elevations, each named for what it is: **TOP** (top of pipe), **SPR** (springline), **BOT** (structure bottom), **WL** (water surface). None of these is ever called an invert. |

Red is pure RGB red (`1 0 0 rg` in the page operators), so it stands out on screen and in colour print and still
reads as a distinct mid-grey on a black-and-white printer; inverts are also bold.

## Where the numbers come from

Nothing is re-derived. Elevations come from the library's `DipElevations` (`Pipe`, `Envelope`, `Bottom`, `Water`) --
the same calculations the Dip Builder window, its labels and its QC use -- and are rounded to the office's elevation
decimals (Settings > Storm & Sewer Dips > Elevation decimals), as the structure labels are. Slopes come from
`SlopeCalculator`; warnings from `UtilityQc`.

- An **invert** is given only for a dip taken to the invert. A top-of-pipe dip gives the **TOP** elevation and says
  the invert is not calculated (the wall thickness is not known); a springline dip likewise.
- A dip that does not say what it was taken to is never given an elevation (and makes the observation incomplete).
- Measured depths are printed with every digit recorded, written to at least hundredths (`6.8` prints `6.80`;
  `6.125` stays `6.125`). Nothing recorded is rounded away.
- Anything nobody recorded prints as *not recorded*. The header values (project, number, date, crew) are typed by
  the drafter from the field book; FieldToFinish never fills them in itself (the project name and number are
  offered from an exhibit in the same drawing when one has them).

## Which observations are included -- "complete"

The Dip Builder has no "mark complete" switch, and the field notes do not add one. An observation counts as
**complete** when its own data says so:

- the structure has its surveyed point (the rim every elevation is calculated from), and that point has not moved or
  changed elevation since the elevations were calculated (otherwise: rebuild on the Review tab first);
- at least one pipe is recorded;
- every pipe has a measure down, or a recorded reason it could not be dipped (UNABLE TO DIP, BLOCKED, SUBMERGED...);
- every measure down says what it was taken to -- stated in the note, by the office convention, or confirmed by the
  drafter;
- no line of the typed note was unreadable.

A missing size, material or direction does not make an observation incomplete; it prints as *not recorded*.

Incomplete observations are never included, even if asked for. In the Save Field Notes PDF window they are listed but
cannot be ticked; in the PDF they are listed on the first page under **Not included -- observation incomplete**, each
with what it is missing. Checking completeness changes nothing in the observation.

**Automatic saving** follows the Dip Builder's finishing step for a structure: placing its structure label (**Place
structure label...**, **Label all structures**, or `FTFDIPLABEL`). When that structure's observation is complete, the
PDF is saved for every complete observation in the drawing. When nothing in the record has changed since the newest
PDF of the same name, nothing is written. An automatic save is never opened, and a problem saving it never undoes the
label that was just placed.

## The plan sketch

North up, not to scale, on a faint field-book grid:

- the structure as a **circle** (round: manholes, round catch basins), a **square** (rectangular: catch basins,
  inlets, vaults), or a **dot** (cleanouts, culvert ends -- structures with no size);
- each pipe as a line in its direction, with an arrow for the flow (toward the structure for IN, away for OUT), and
  its P-number, size, material, **INV** (red) and the structure it connects to;
- a **north arrow** whenever any pipe is drawn.

Only directions **recorded at the structure** are drawn solid. A direction that was not observed there is drawn
**dashed** and labelled **(inferred)**: one carried over from the connected pipe's other end, or -- for a pipe with no
recorded direction that the drafter connected to another surveyed structure -- the surveyed line to that structure.
A pipe with no direction and no connection is not drawn at all; the sketch lists it ("Not drawn, no direction
recorded: P3"). The table always shows the direction exactly as recorded (*not recorded* when there is none).

## File name, folder and revisions

**Name**: `[project]_Utility-Dip-Field-Notes_[date of observation].pdf`, for example
`26-104_Utility-Dip-Field-Notes_2026-10-28.pdf`. The project is the project number, else the project name, else the
drawing's name; the date is `undated` when none was recorded. Characters Windows does not allow become `-`.

**Folder**: Settings > Storm & Sewer Dips > Field notes PDF > *Save in*. Default `Survey\Field Notes\Utility Dips`,
relative to the project folder, which is found from the drawing:

1. a drawing inside a folder named like the setting's first part (`P:\Jobs\26-104\Survey\CAD\base.dwg`): the project
   folder is the one above it (`P:\Jobs\26-104`);
2. otherwise the nearest of the drawing's folder and its two parents that already has such a folder beside the
   drawing (`P:\Jobs\26-104\CAD\base.dwg` with `P:\Jobs\26-104\Survey`);
3. otherwise the drawing's own folder.

An absolute folder (`P:\Field Notes`, `\\server\share\Dips`) is used as it is. The folder is created when needed. The
Save Field Notes PDF window shows the folder and the exact file it will write, and can browse to another. An unsaved
drawing has no project folder: save it first, or browse to a folder.

**Revisions**: an issued PDF is never overwritten. When the file already exists the new one is the next revision --
`_R1`, `_R2`, one past the highest there -- and its first page says which file it supersedes. The earlier issues are
kept. Overwriting is only ever explicit: *Replace the newest field notes PDF instead of saving a new revision* in the
settings, or the same box in the window (which asks before replacing).

## Settings

Settings > Storm & Sewer Dips > **Field notes PDF** (stored in the Dips section of the FTF settings):

| Setting | JSON | Default |
| --- | --- | --- |
| Save in | `fieldNotesFolder` | `Survey\Field Notes\Utility Dips` |
| Save the field notes automatically when a structure is finished | `fieldNotesAutoGenerate` | off |
| Replace the newest field notes PDF instead of saving a new revision | `fieldNotesOverwrite` | off |
| Open the PDF after Save Field Notes PDF | `fieldNotesOpenAfterSave` | on |
| Draw a plan sketch of each structure and its pipes | `fieldNotesSketches` | on |

The header the drafter types (project name and number, date of observation, crew) is kept **in the drawing** (an
Xrecord `DIPFIELDNOTES` under FTF's dictionary, beside the dip project), so the next save -- or an automatic one --
starts from it.

## Code

| Where | What |
| --- | --- |
| `src/FieldCodes/Utilities/FieldNotes/FieldNotesModel.cs` | The record's content model, the header, and `DipObservationStatus` (completeness). |
| `src/FieldCodes/Utilities/FieldNotes/FieldNotesReportBuilder.cs` | Reads the dip project into the record through `DipElevations`, `SlopeCalculator`, `UtilityQc`. |
| `src/FieldCodes/Utilities/FieldNotes/FieldNotesLayout.cs` | Pure page layout: what goes where, in which font and colour. |
| `src/FieldCodes/Utilities/FieldNotes/PageLayout.cs` | Page operations, colours, text roles, font metrics, WinAnsi text. |
| `src/FieldCodes/Utilities/FieldNotes/PdfWriter.cs` | The PDF 1.4 writer (xref, trailer, Flate streams). |
| `src/FieldCodes/Utilities/FieldNotes/FieldNotesFiles.cs` | Folder, file name and revision rules; reading the content fingerprint back. |
| `src/FieldCodes/Utilities/FieldNotes/DipFieldNotes.cs` | Render and save (never over an issued file unless asked). |
| `src/FieldCodes.Cad/UtilityCommands.cs` | `FTFDIPPDF`, the shared save, and the automatic save after a label is placed. |
| `src/FieldCodes.Cad/DipFieldNotesStore.cs` | The header kept in the drawing. |
| `src/FieldCodes.Cad/Ui/DipFieldNotesDialog.cs` | The Save Field Notes PDF window. |
| `tests/FieldCodes.Tests/DipFieldNotes*Tests.cs` | Content, colours, completeness, sketch, PDF structure, names, revisions, folders. |

## Not verified here

Everything above the CAD layer is covered by the unit tests, and generated PDFs were checked with `pdfinfo` and by
rendering them with `pdftoppm`. The Dip Builder buttons, the window, `FTFDIPPDF`, the automatic save and the header
kept in the drawing compile against the Civil 3D API stand-ins but have **not been run in Civil 3D 2024** yet.
