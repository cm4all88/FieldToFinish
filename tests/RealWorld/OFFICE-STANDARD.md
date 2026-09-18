# PMX survey exhibit standard, as measured

Measured with STDDUMP/BLKDUMP/GEODUMP (tests\RealWorld) from **copies** of delivered exhibits and the office
C3D 2024 templates. The originals on U: were only read. `make-office-profile.ps1` turns these numbers into
`config\profiles\PMX SURVEY EXHIBIT.json` and `PMX SURVEY EXHIBIT TABLES.json`. FTF's generic default profile is unchanged.

## Sources

| What | Where |
|---|---|
| Office template | `U:\PSO\Shared\Divisions\00Survey\OFC_RSC\Equipment & Software\Autocad\C3D2024\Templates\PMX Survey Standards C3D.dwt` |
| Layout and sheet blocks | `...\Templates\SheetSets\PMX-Survey-Layouts.dwt`, layout "8x11 EXHIBIT"; blocks PMXLogo, G-ScalebarFig, PMX11x17SurvBorder, PMXSurv22X34Border |
| Stamps | `...\Symbols\pmxSurveyStamps.dwg` (pmxSurveyStamp, NAME_PLS_*) |
| Plot styles | `C:\PMX\CADD\C3D2024\Plotting\PlotStyles` (Civil 3D profile "PMX Survey Civil3D 2024") |
| Reference exhibit | Silver Lake 9171, `SV-2169171001-ESMT-28052700104100.dwg` (strip), plus 104300 (permanent + TCE) |
| Second office example | Kenmore 554-3744-009, `SV-554-3744-009-EXH-01141001xx.dwg` with delivered and QC PDFs |

Local copies are in `C:\dev\FTF-RealWorld\refs`.

## Sheet and plot

| Item | Office value |
|---|---|
| Paper | 8.5 x 11 portrait, `ANSI_full_bleed_A_(8.50_x_11.00_Inches)` |
| Plotter | `AutoCAD PDF (High Quality Print).pc3` |
| Plot style table | `PMX Survey BW.ctb` |
| Layout scale | 1:1 |
| Border | polyline 1.01,1.01 to 7.51,10.01 on G-BORD-XLIT |
| Title | MText, style Border3 (Arial Bold), 0.14, TopCenter at 4.261,9.857, width 6.493; first line at 1.42857x: `EXHIBIT B` / aliquot / county / easement type |
| Scale bar and north arrow | dynamic block G-ScalebarFig at 4.839,1.399; visibility state `1" = 60'` etc. |
| Logo | PMXLogo, Visibility1 = office (Puyallup, Seattle, ...); Kenmore used pmx-logo-sea at 0.984,2.238 |
| Stamp | WA-stamp-here placeholder at 6.512,2.06 (not in the office library) |

## Viewports

| | Silver Lake | Kenmore |
|---|---|---|
| Layer | XX-VPRT (no plot) | 00-VP (no plot) |
| Size | 6.39 x 6.996, center 4.265,5.42 | 6.374 x 5.747 from 1.043,3.21, clipped |
| Scale | 1" = 60' | per exhibit |
| Frozen in viewport | V-CTRL-OTHE-PNTS-E, AP-LOT, AP-CENTERLINE, V-UTIL-POWR-OVHD-E | other exhibits' V-TEXT-FEE/ESMT/TCE, C-BNDY-LIMT*, C-PROP-RWAY-PATT* |

Office layers are hidden **per viewport**, never frozen globally. FTF does the same through `ViewportLayerRules`.

## Annotation

| Item | Office value |
|---|---|
| Text | style "Survey" (romans, 0.75 width), 0.08 |
| Leaders | mleader style "xPMX SURV Text Arrow Anno": "SW COR PARCEL 3\P(POINT OF COMMENCEMENT)", "POINT OF BEGINNING", "POINT OF TERMINUS" |
| Dimensions | "PMX SURV ANNO" (annotative, 2 places, suffix ') |
| Easement layers in the template | V-ESMT-E, V-ESMT-TEXT-E, V-ESMT-PNTS-E only |
| Hatch | permanent ANSI31 red on V-ESMT-E (cyan HIDDEN2 outline); Kenmore TCE ANSI37 |
| Model annotation | annotative; parcel labels in italic styles; APN in a rounded box |

## Where the office is not consistent (surveyor or CAD manager decision)

- **Area wording.** "APPROX EASEMENT AREA = 8,458 SF" (104100), "APPROX. EASEMENT AREA= 4,181 SF" (104300),
  "TEMPORARY CONST. ESMT. (2103 SQ. FT.)" legend (Kenmore). The profile uses `APPROX {purpose} EASEMENT AREA = {sqft} SF`.
- **Line table headers.** "LINE NO. | DISTANCE | BEARING" (Silver Lake) and "LINE NO. | LENGTH | DIRECTION" (Kenmore).
  The profile uses Silver Lake's.
- **Curve table.** "CURVE NO. | LENGTH | RADIUS | DELTA".
- **Layout.** A tall viewport with the area label inside (Silver Lake), or a shorter viewport with tables below (Kenmore).
  Hence the two profiles.
- **Logo office.** Puyallup is set in the profile as a placeholder; each project office sets its own.

## Production cleanup choices (2026-09-17) -- confirm with the office

Each is a profile setting, taken from what the delivered exhibits show; none is forced on other profiles.

| Setting | PMX value | Evidence |
|---|---|---|
| North arrow rotation property | `Angle1` | G-ScalebarFig's rotation parameter turns only its arrow (BLKDUMP of PMX-Survey-Layouts.dwt); FTF checks the turn from the block geometry |
| Hatch spacing by pattern | ANSI31 0.03125", ANSI37 0.0977" | 104100: ANSI31 annotative 0.25 at 1" = 60'; Kenmore: ANSI37 at 31.25, 1" = 40' |
| Overhead power | Hide (`V-UTIL-POWR-OVHD*`) | 104100 froze V-UTIL-POWR-OVHD-E in its viewport |
| Other exhibits' hatches | Relevant (`C-PROP-RWAY-PATT*; C-BNDY-LIMT-PATT*`) | Kenmore froze other exhibits' fee and TCE hatches per viewport |
| Stamp | Placeholder at 6.512, 2.06 | Kenmore's WA-stamp-here place; FTFEXHIBITSTAMP offers `Symbols\pmxSurveyStamps.dwg` when the profile is set to Block |
| Area wording (easements) | `APPROX {purpose} EASEMENT AREA = {sqft} SF` | construction/acquisition areas keep their own area wording |
| Line table headings | DISTANCE/BEARING (Silver Lake); LENGTH/DIRECTION (Kenmore TABLES profile) | both appear on delivered exhibits |
