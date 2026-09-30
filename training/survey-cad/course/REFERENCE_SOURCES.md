# Command Reference: verification notes

Commands and behavior in `reference_content.js` were checked against Autodesk help (help.autodesk.com,
AutoCAD and Civil 3D 2024-2026) in September 2026. The check used Autodesk's indexed help pages through
web search. The research environment could not open every page in full, so entries marked "not confirmed"
below are taught by their ribbon or Toolspace path instead of a typed name. AutoCAD help links follow the
pattern `help.autodesk.com/view/ACD/2025/ENU/?guid=<GUID>`; Civil 3D links use `view/CIV3D/<year>/ENU/`.

## Corrections made to the previous command sheet

| Was | Now | Source |
|---|---|---|
| PSLTSCALE shown as 0 with LTSCALE 20, as if it were a general rule | Autodesk default is 1: dashes are the same paper size in every viewport. 0 makes dashes follow LTSCALE in model units. The training drawing uses 0 with LTSCALE 20, and the reference now labels that as a training setting | ACD GUID-23EA4D64-AE7D-41E5-A8D0-20F060313D62 |
| OSNAPZ 1 as a plain setting | 0 is the default and snaps take the point's Z. The variable is not saved, so it resets every session. Now carries a warning about point elevations | ACD GUID-5A49F08E-C0FD-4B38-9253-BC60197E1A60 |
| PICKADD 2 / PICKDRAG 0 as fixes | Current defaults are PICKADD 2 and PICKDRAG 2 (both click methods). The reference now restores defaults | ACD GUID-47C2A568-30EE-4F07-916F-884CDE25CBCA, GUID-46CBF4EF-E210-447D-AE6E-793249551640 |
| TRIMEXTENDMODE 0 described loosely | 1 = Quick (default since 2021). 0 = Standard (select the edges first) | ACD GUID-52B3803E-FADC-4D7B-9BF2-A176C7FB7535 |
| GRIPS 1 | Default is 2: grips plus polyline midpoint grips | ACD GRIPS |
| Cycle overlaps: Shift + Space | Selection cycling, CTRL+W (SELECTIONCYCLING) | ACD GUID-08FA6975-49A7-4029-A3F3-ABDA3B2A7122 |
| Model/layout tabs only through Options | LAYOUTTAB 1 also works | ACD GUID-C340CB7C-F65B-4603-B8F6-9B1C6B5B5E2A |
| TXT2MTXT as an Express Tool | Core command: Insert › Import › Combine Text | ACD TXT2MTXT |
| Toolspace: TOOLSPACE / SHOWTS | SHOWTS confirmed; TOOLSPACE not confirmed, so dropped | CIV3D 2025 GUID-79A7FC01-C932-4869-A27D-C863A7A10AA5 |
| Transparent commands on "the Transparent Commands toolbar" | A Transparent ribbon tab appears while a command runs; right-click › Transparent Commands also works | CIV3D 2025 GUID-080F5365-7810-4D10-9873-1ACBD15A3CED |
| 'NE: "type N first, then E" | Northing first by default; Ambient Settings › Transparent Commands › "Prompt For Easting Then Northing" can swap the order | CIV3D 2025 GUID-6861256C-7660-4B7B-A332-0DFB4B06D972 |

## Checked but not used, or taught by menu path

- **'GR**: not found as a plan transparent command. Not included.
- **'STA**: Station and Offset exists, but the abbreviation could not be confirmed. Not included.
- **SHOWINQUIRY, MAPCHECK, HIDETS, Process Linework**: the features are confirmed; the typed names are not. Taught as
  Analyze › Inquiry › Inquiry Tool, Analyze › Ground Data › Survey › Mapcheck, and Survey tab › right-click › Process Linework.
- **VALIDATESHORTCUTS, SYNCHRONIZEREFERENCES, REPAIRBROKENREFERENCES**: not documented as typed commands. They are Prospector
  right-click items: Validate Data Shortcuts (top Data Shortcuts node), Synchronize (on an out-of-date reference), and
  Repair Broken References. (CIV3D GUID-9E19213A-C396-4F8A-B356-B8AC45EDC99E, GUID-B079A088-6594-4095-A50B-7A8FC5770C5C,
  GUID-56502A1D-0E87-4E8D-A3A8-FEF3B5F432C3)
- **CREATEREFERENCE, DATASHORTCUTEDITOR**: the first is an API method only; use Prospector › Create Reference. The second is the
  Data Shortcuts Editor, a separate Windows application (GUID-4CE64104-7C0D-4BC6-8E42-D377A805AD87).
- **LAYLOCK / LAYUNLOCK**: not AutoCAD commands. The reference uses LAYLCK / LAYULK.
- **MLEADERCOLLECT**: works only on multileaders with block content, so it is not useful for survey text labels. Not included.

## New content and where it was confirmed

| Item | Source |
|---|---|
| Window (left to right) vs crossing (right to left), lasso and Spacebar cycling, PICKAUTO | ACD GUID-D0D5C0C3-F092-448A-8E81-D38F27094639, GUID-7BAAA374-8409-4296-B520-C5355941C836 |
| DYNPICOORDS (typed second points are relative; # forces absolute) | ACD GUID-87D649DF-8474-4F55-806E-604906030B36 |
| LAYERSTATE, COPYTOLAYER, ISOLATEOBJECTS, PAGESETUP, PUBLISH, GEOGRAPHICLOCATION | ACD command reference (GEOGRAPHICLOCATION: GUID-10A3B776-A0FA-4438-B29B-EA22C070A27E) |
| OVERKILL options and risks ("Do not break polylines", combine co-linear) | ACD OVERKILL |
| EDITDRAWINGSETTINGS tabs: Units and Zone, Transformation, Object Layers, Abbreviations, Ambient Settings | CIV3D GUID-55098BE5-8C91-45F7-AC51-F1E3EAD5B834, GUID-C81280B7-3169-43EC-B3DD-B4677743B797, GUID-A68DFB6F-6BE3-4719-8A1E-2D4FF1E53219 |
| 'BD quadrants 1 NE, 2 SE, 3 SW, 4 NW; bearings in the drawing's angle format | CIV3D 2024 GUID-A617A016-D166-4C87-8AFE-8A2A6FA896A8, Ambient tab GUID-21B00B39-A954-4E71-9B2B-AFCFAA9DA6E2 |
| 'ZD, 'PN, 'PO, 'SS, 'ZTP, 'MR, 'ML | CIV3D GUID-4270870C-2CDC-49B0-BA90-E07DEA365C0E, GUID-246878BA-5FF5-4F3F-A01A-0CA5136FA869, GUID-3CEBED82-EFCF-4EC0-BBBF-B300BCB0CFE5, GUID-C22FAFB1-3EBB-415B-92C7-65A0B927EF0C |
| CREATEPOINTS, IMPORTPOINTS, Point Editor, Lock/Unlock points | CIV3D GUID-720B9C25-9BDC-4AB8-B7AA-86DEF6484B0F, GUID-3CB9DB32-05E8-4B0E-BBC9-5A58BCDE2D12, GUID-5AC17D2A-2AA1-4FE0-8360-434BC4023059, GUID-7F434930-9236-40D7-8A27-6A0591D1D9B1 |
| ADDSEGMENTLABELS / ADDSEGMENTLABEL | CIV3D GUID-68B4CC32-6624-4A37-B02E-DCEB4F8B8839 |
| Point group display order, Update out-of-date groups | CIV3D GUID-92334299-2A6B-4386-9815-D31F80309D1D |
| Description keys | CIV3D GUID-A411F11B-2546-4950-8D5D-FE2FDAE7E75D |
| Label dragged state, Reset Label | CIV3D GUID-8B7E3D3E-6815-4043-BB95-4F24EEAD945C |
| "???" in labels | Autodesk KB "Alignment labels show question marks in Civil 3D" |
| Point Table; Line/Curve tables and tag mode | CIV3D GUID-8BCE4FE6-7986-4F40-AD1B-516675F31474, GUID-9FD82A29-BDC8-47B4-9473-57282372DD3F |
| Survey figures, figure prefix database, linework code sets | CIV3D GUID-E2E68A54-E69B-486E-9596-41B9960C559A, GUID-ED29E1F3-2A73-47E6-A1AF-E1972E7E2203, GUID-C2269A52-FFF0-4C3F-91DA-649E623E1EAC |
| Surface boundaries, Rebuild / Rebuild - Automatic | CIV3D GUID-3EF4691D-2645-4357-819C-E3FA3E14BF21, GUID-EA66F3B2-5C3B-4397-92F9-04869060FEAA |
| OBJECTVIEWER, Inquiry Tool, Mapcheck | CIV3D GUID-AD83228A-E5F7-4250-8AE6-67D0E7444738, GUID-34D357E8-3F99-4979-851E-8CF241661F8F, GUID-724CDC76-36F5-4482-882A-528D40E91973 |
| Sites: parcels and feature lines interact | CIV3D GUID-FB52555F-4738-4BCB-B86C-8EDCFF3A9A7B |
| CREATEPARCELFROMOBJECTS ("Erase existing entities") | CIV3D GUID-21632A30-C4B6-4DBB-BA07-1C323504ADB2 |
| MAPCLEAN, EXPORTTOAUTOCAD (ACAD- prefix, original untouched), PURGESTYLES | CIV3D GUID-B68B26D6-8A58-4E40-9C6F-B7715D357DF0, GUID-730CB0EA-31A1-481C-BF0F-BD7C926FDC05, GUID-9328A4A9-AE72-438D-9D48-631A282A8B2B |
| Civil 3D label heights in plotted units | CIV3D GUID-2F513449-443A-4057-A821-5DBFB2262118 |
| Data shortcuts: objects that can be shortcut (no COGO points or point groups), Promote, SETWORKINGFOLDER, SETSHORTCUTSFOLDER, CREATEDATASHORTCUTS | CIV3D GUID-E91D2116-0F01-4A6B-B9C6-A47ED56F0F5F, GUID-16B3592C-1542-4251-A2FE-AB0EE9807268, GUID-0AC8F3A4-13D5-4C75-8CF0-64AD071CB2D9 |
| Labeling Civil 3D objects in xrefs (xref at 0,0,0, scale 1, rotation 0) | CIV3D GUID-8B68111E-3E88-481A-A265-127146AE4B46 |
| Xref attach vs overlay, path types, NAME\|LAYER | ACD GUID-A987D2FF-45BD-474E-99C1-E6316A42F667 |

## Parametrix-specific content and where it comes from

- Linework codes (B, E, C, P/T, U, G, CIR, X) and figure prefix database name: `config/rules.json`
  (from the office PMX-Universal linework code set and the Civil3D parametrix figure prefix database).
- Label formats (`CB #10012 / RIM= / IE ... / BOTTOM=`, `22" CFR / 25' DRIP`, `PMX #2000 / MAG NAIL`, `FFE=`) and
  V- layer names: the Parametrix survey base drawing used for the training course.
- Text height 1.6' (0.08" at 1" = 20'), SRV-20 styles, V-ANNO-DIMS-E, monochrome.ctb, LTSCALE 20 / PSLTSCALE 0:
  the Survey CAD training drawings. The reference marks these as training settings to confirm against the Parametrix template.

## Added from a Parametrix drafter's list (checked September 2026)

| Item | Result | Source |
|---|---|---|
| SNAPANG | Confirmed. It sets the snap/crosshair angle for the current viewport; it doesn't follow a turned view automatically | ACD GUID-7C4EAEAE-3738-4E51-AC9B-B16B5A2CDB3B |
| ZOOMFACTOR | Confirmed: 3 to 100, default 60 | ACD GUID-6A77AD55-6035-42FF-8FB1-FB0D8EFE1278 |
| WBLOCK for a crew DXF | Corrected. WBLOCK writes a DWG (unless the default save format is DXF). Use SAVEAS › Tools › Options › DXF Options › Select objects | ACD GUID-F8F9ADB8-8011-42B0-86D9-80BFAA9B23CE, GUID-0FC24222-2C6A-4068-8ECF-DB7DDCEE6656 |
| MVIEW Polygonal / Object | Confirmed | ACD GUID-731B2752-B9E2-443E-816A-9B4851296455 |
| QAFLAGS 0 | Undocumented internal variable; community reports only. Listed after PICKFIRST as a last resort | forums.autodesk.com |
| LEVELOFDETAIL / LEVELOFDETAILOFF | Corrected name (LEVELOFDETAIL turns it on). It reduces surface detail when zoomed out; turn it off if contours are missing | CIV3D GUID-F715FE3C-66CB-458B-848F-8743B2760F07 |
| MAPIMPORT | Confirmed (SHP, MIF, TAB) | MAP GUID-D65473F6-0B63-4F4E-A3B7-9B8EE8217B77; CIV3D GUID-A8EAA81C-778A-4B39-8BFC-1C699FFB362D |
| EXPORTKML | Confirmed in Civil 3D; writes KML or KMZ; needs a coordinate system. Map 3D uses MAPEXPORT | CIV3D GUID-D31F08CA-EFFF-4A04-B426-7ABACCBCB3A8 |
| SELECTIONANNODISPLAY | Confirmed; default 1 = other scale versions shown dimmed | ACD GUID-022C086E-E1E2-4FB2-A7B3-4272EC810FA9 |
| LINETYPE3DPLINEON / OFF | Civil 3D only, from an Autodesk KB (not in the help). Pattern follows the 3D length; PLINEGEN not honored | KB "Linetype for 3D polyline" |
| OFFSETFEATURE | Confirmed. Creates a feature line (not a 3D polyline) offset from a feature line, figure, 2D or 3D polyline | CIV3D GUID-5D15FCE9-148D-484D-9E70-91DA6DBD0C78 |
| LISTA / LISTU | Corrected to LISTAVAILABLEPOINTNUMBERS / LISTUSEDPOINTNUMBERS. "Available" means unused numbers. LISTA/LISTU are probably office aliases | CIV3D GUID-6029E9E4-55AC-4C10-BF80-04B5F3F8E2AA |

## Coordinate system, transformation and online maps (checked September 2026)

| Item | Result | Source |
|---|---|---|
| Units and Zone tab: linear and angular units, drawing scale, coordinate system | Confirmed | CIV3D GUID-C81280B7-3169-43EC-B3DD-B4677743B797, GUID-A2C0D8F7-0BCF-46F6-8A0E-217834AF136B |
| Transformation tab: local to grid; sea level and grid scale factor (Unity, User Defined, Reference Point, Prismoidal); needs a zone first | Confirmed | CIV3D GUID-A68DFB6F-6BE3-4719-8A1E-2D4FF1E53219, GUID-493452B6-14D3-46DB-AF12-AF1DBB2D168D |
| Geolocation tab appears after a coordinate system is assigned; online maps need an Autodesk sign-in | Confirmed | CIV3D GUID-7B90BBB6-DA3A-4022-BCA9-6A1F03CCEE05 |
| GEOMAP: Aerial / Road / Hybrid / Off | Confirmed | ACD GUID-DEF7EA7B-6A4B-4520-9A6F-08A944F78218 |
| Online map doesn't plot; GEOMAPIMAGE captures a plottable map image (plan view of WCS); GEOMAPIMAGEUPDATE | Confirmed | ACD GUID-092F0E5E-BBA9-4A4E-ACF0-4779808C4384, GUID-79DFA423-8546-4DD4-9A23-1C0F0B9DF607, FAQ GUID-32DAF2C8-DBC9-4459-BC6D-08BDC5A9ED21 |
