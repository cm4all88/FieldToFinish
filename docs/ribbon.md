# The FTF ribbon

Four tabs, one per kind of work. The layout lives in
`src/FieldCodes/Commands/RibbonLayout.cs` and is checked by
`tests/FieldCodes.Tests/RibbonLayoutTests.cs`; `src/FieldCodes.Cad/FtfRibbon.cs` builds the
ribbon from it and draws the icons. Every button's tooltip says what it does and shows the
command it types, so any button can also be typed.

| Tab | For |
|---|---|
| **FieldToFinish** | Finishing the survey drawing: process it, points and symbols, labels, trees, utility features, settings, reports |
| **Survey Drafting** | Drafting on top of it: spot elevations, contours, line labels, drafting utilities, sheets |
| **Boundary** | Record work: recorded surveys, calculations and closure, right of way and platted blocks, the base-map comparison |
| **Easements** | Creating easements, their legal descriptions, exhibits and closure checks |

## FieldToFinish

| Panel | Button | Command |
|---|---|---|
| Process | Process Drawing (large) | `FTFRUN` |
| | FTF Window (large) | `FTF` |
| | Draw Order | `FTFORDER` |
| | Clean Up | `FTFCLEAN` |
| Points & Symbols | Points (large) | `FTFPOINTS` |
| | Orient Symbols (large) | `FTFORIENT` |
| | Reset Orient | `FTFORIENTRESET` |
| | Control Table | `FTFCONTROL` |
| | Legend | `FTFLEGEND` |
| Labels | Label Everything (large) | `FTFLABELALL` |
| | Point Labels | `FTFLABELS` |
| | Update Styles | `FTFRESTYLE` |
| Trees | Driplines (large) | `FTFDRIP` |
| | Tree Tags | `FTFTAGS` |
| | Tree Schedule | `FTFTABLE` |
| Utility Features | Dip Builder (large) | `FTFDIP` |
| | Check Dips | `FTFDIPCHECK` |
| | Field Notes PDF | `FTFDIPPDF` |
| Settings | Settings (large) | `FTFSETUP` |
| | Profile | `FTFPROFILE` |
| | Version | `FTFVERSION` |
| | Office Update | `FTFUPDATE` |
| Reports | Linework | `FTFLINES` |
| | Blocks | `FTFBLOCKS` |
| | Rules File | `FTFWHERE` |

## Survey Drafting

| Panel | Button | Command |
|---|---|---|
| Elevations | Spot Elevations (large) | `FTFSPOT` |
| | Contours (large) | `FTFCONTOUR` |
| Line Labels | Label Line (large) | `FTFLABELLINE` |
| | Between (large) | `FTFLABELBETWEEN` |
| | Driveway (large) | `FTFLABELDRIVEWAY` |
| | Stairs (large) | `FTFLABELSTAIRS` |
| Drafting | Draw Lines (large) | `FTFDRAWLINE` |
| | Break Mark | `FTFTILDE` |
| | ROW Width | `FTFROWDIM` |
| | Bench | `FTFBENCH` |
| | Clean Drafting | `FTFDRAFTCLEAN` |
| Sheets | Plan Sheets (large) | `FTFSHEETPLAN` |
| | Make Layouts | `FTFSHEETMAKE` |
| | From Layouts | `FTFSHEETS` |
| | Key Map | `FTFKEYMAP` |
| | Match Line | `FTFMATCHLINE` |

## Boundary

| Panel | Button | Command |
|---|---|---|
| Recorded Surveys | Recorded Survey (large) | `FTFRECORD` |
| | Rebuild Record | `FTFRECORDREBUILD` |
| | Record Labels | `FTFRECORDLABEL` |
| | Line Source | `FTFRECORDSOURCE` |
| Calculations & Closure | Draw Lines (large) | `FTFDRAWLINE` |
| | Check vs Record (large) | `FTFRECORDCHECK` |
| | Clean Drafting | `FTFDRAFTCLEAN` |
| Right of Way & Lots | Right of Way (large) | `FTFROW` |
| | Platted Block (large) | `FTFLOTS` |
| | ROW Width | `FTFROWDIM` |
| Record Comparison | Compare Survey (large) | `FTFCOMPARE` |
| | Trim Aerial (large) | `FTFTRIM` |
| | Run Compare | `FTFCOMPARERUN` |
| | Apply | `FTFCOMPAREAPPLY` |
| | Final QC | `FTFCOMPAREQC` |
| | Save Deliverable | `FTFCOMPARESAVE` |
| | Batch Sites | `FTFCOMPAREBATCH` |
| | Restore Compare | `FTFCOMPARERESTORE` |
| | Restore Trim | `FTFTRIMRESTORE` |

## Easements

| Panel | Button | Command |
|---|---|---|
| Create | Strip Easement (large) | `STRIPEASEMENT` |
| | Portion Easement (large) | `PORTIONEASEMENT` |
| | Area Easement (large) | `CONSTRUCTIONAREA` |
| | Exclude Area | `FTFEASEMENTEXCLUDE` |
| | Add Component | `FTFEASEMENTCOMPONENT` |
| Legal Descriptions | Legal Draft (large) | `FTFEASEMENTLEGAL` |
| | Export Courses | `FTFEASEMENTEXPORT` |
| Exhibits | Exhibit (large) | `FTFEXHIBIT` |
| | Rebuild Exhibit | `FTFEXHIBITREBUILD` |
| | Exhibit Group | `FTFEASEMENTGROUP` |
| | Exhibit QA | `FTFEXHIBITQA` |
| | Preview PDF | `FTFEXHIBITPREVIEW` |
| | Stamp | `FTFEXHIBITSTAMP` |
| Closure Verification | Inspect Easement (large) | `FTFEASEMENTINSPECT` |
| | Check Easements (large) | `FTFEASEMENTCHECK` |
| | Inspect Exhibit | `FTFEXHIBITINSPECT` |

## Notes

- **On two tabs on purpose:** Draw Lines (`FTFDRAWLINE`), Clean Drafting (`FTFDRAFTCLEAN`) and
  ROW Width (`FTFROWDIM`) are both drafting and boundary work, so they sit on Survey Drafting and
  on Boundary. No command appears twice on one tab.
- **Record Comparison** holds the `FTFCOMPARE` family: our survey against a consultant's base map
  for an ACC deliverable, plus trimming the aerial base. The check of the drawing against a
  recorded plat is Check vs Record (`FTFRECORDCHECK`), under Calculations & Closure.
- **Icons:** every command has its own. A family shares a glyph and tells its members apart
  with a badge in the lower right corner: a tick checks, a circling arrow rebuilds, a turning
  arrow puts back, a magnifier inspects, plus adds, minus takes away, a play mark runs, an eraser
  cleans, an outgoing arrow exports.
- **Typed, not on the ribbon:** short aliases (`FTFL`, `FTFB`, `FTFS`, `FTFDWY`, `FTFPORTION`,
  `FTFAREA`, `FTFEASEMENT`, `FTFTREES`, `FTFSETTINGS`, `FTFCONFIG`, `FTFOPTIONS`); steps the Dip
  Builder and Compare windows already have buttons for (`FTFDIPNOTES`, `FTFDIPCONNECT`,
  `FTFDIPCONFIRM`, `FTFDIPDRAW`, `FTFDIPLABEL`, `FTFDIPINSPECT`, `FTFDIPREVISIT`,
  `FTFCOMPARESOURCE`, `FTFCOMPAREPROFILE`, `FTFCOMPAREAREA`, `FTFCOMPAREDECIDE`,
  `FTFCOMPAREAUDIT`); diagnostics (`FTFLINEMETA`, `FTFLINELABELS`); and the windows' internal
  `FTFDIPACT` and `FTFCOMPAREACT`, which are not meant to be typed.
- **Tab ids** stay stable (`FTF_RIBBON_TAB` is the FieldToFinish tab and `FTF_BOUNDARY_RIBBON_TAB`
  the Boundary tab, as before), so a reload replaces the tabs rather than adding a second set.
  An id a later build stops using goes in `RibbonLayout.RetiredTabIds`, so it is taken off too.
- **Changing the ribbon:** edit `RibbonLayout.cs` (and this page), add any new icon as a
  `public static ImageSource Name(int px)` method in `FtfIcons`, and run the tests. A command
  that was on the ribbon must stay on it; a command name is never changed.
