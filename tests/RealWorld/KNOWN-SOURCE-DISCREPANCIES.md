# Known source-office discrepancies

These are differences between the **office's own delivered drawings or legals** and the geometry they describe. They
were found while testing FTF against real Parametrix work. They are **not targets for FTF to reproduce**:

- FTF stays mathematically and survey correct.
- FTF reports what it calculates and flags where the source disagrees.
- The surveyor decides which record governs.

Each entry names the source file and the evidence. Where noted, the evidence comes from the text dumps in
`C:\dev\FTF-RealWorld\dumps\`, made from the office drawings (the drawings themselves are not in this repo).
First recorded in the real-world testing of 2026-09-16/17.

| # | Case | Source says | Geometry / FTF says | Evidence |
|---|---|---|---|---|
| 1 | 104100 sewer line easement | Area text "APPROX EASEMENT AREA = 8,458 SF" | The office's own hatch encloses 8,447.59 sq ft; FTF calculates 8,447.15 and labels 8,447 SF | `dumps\SV-2169171001-ESMT-28052700104100.txt:608` (text), `:561` (hatch area); `compare-*.md` |
| 2 | 104100 legal | Reads "PORTION OF PORTION OF", "25.00 FEET IN WIDTH, LYING 10.00 FEET ON EACH SIDE" (10 + 10 is not 25), and commences at the SE corner | The exhibit's leader marks the **SW** corner of Parcel 3 as the point of commencement | Office legal text; exhibit leader `SW COR PARCEL 3 (POINT OF COMMENCEMENT)` at `...104100.txt:526-568` |
| 3 | Kenmore 710 TCE legal | "SOUTH 00°15'34" EAST 43.86 FEET" | The drawing's course is S00°15'34"**W** 43.86' (FTF labels S00°15'34"W) | Office legal vs `runs\c4-kenmore-710-curve-tce` line table |
| 4 | 104300 permanent easement | April version: 2,133 SF on the old alignment | May version: 15' off the section line, 4,181 SF (office hatch 4,181.03) | `dumps\SV-2169171001-ESMT-28052700104300.txt:558` (hatch), `:561` (text) |
| 5 | Kenmore 163 TCE | Legend and legal: "(2103 SQ. FT.)" | The office hatch encloses 2,273.2 sq ft; FTF calculates 2,273 SF | `dumps\SV-554-3744-009-EXH-0114100163.txt` (hatch vs legend); `OFFICE-STANDARD.md:59`; `ProductionCleanupTests.cs` keeps the office wording test |
| 6 | Kenmore 163 fee acquisition area | A ~3 sq ft fee area | Its stated courses close only to about 1:1,712. FTF reports this as a LEGAL REPRODUCTION error rather than forcing a closure | `runs\c7-kenmore-crowded\report.txt` (fee area, 3 courses) |

How FTF treats these:
- Areas: FTF labels what the geometry encloses and never copies an office figure that the geometry does not support.
- Courses: bearings come from the drawn geometry, and a legal that disagrees is the surveyor's to reconcile.
- Closure: a stated course set that does not close is an error in QA, never adjusted silently.
- Different versions of the same exhibit (4): FTF works from the drawing it is given and does not choose between versions.
