# Rotation, partial roadways and annotation: review and corrections (November 2026)

The request described five screenshots from a survey drawing. They showed questionable rotations,
oversized annotation, overlapping labels, and a large "HYDRANT LATERAL (INFERRED)" label with
inferred utility graphics. **The screenshots did not reach the development session:** nothing
was attached, and no image was in the repository or the session. So the analysis below works
from the written description and the code. Each finding says which command makes the object and
why it came out that way. Which one each screenshot shows still has to be confirmed in the
drawing; `FTFWHAT` (below) does that in one step.

## Which command makes what

| What is on screen | Made by | How to tell |
|---|---|---|
| A COGO point's symbol (marker) | Civil 3D, from the point's **point style**. FTF never draws it; FTFORIENT only turns it | `FTFWHAT` on it: "Civil 3D: COGO point ...; symbol from point style ..." and whether FTFORIENT turned it, from what |
| The point's own label (number, description) | Civil 3D, from the point's **point label style** | `FTFWHAT`: "label from point label style ..." |
| A text or multileader label beside a point | **FTFLABELS / FTFLABELALL** | `FTFWHAT`: "FieldToFinish: FTFLABELS / FTFLABELALL: a point label" |
| A symbol block inserted at a point | **FTFPOINTS** (opt-in block rules) | `FTFWHAT`: "FTFPOINTS: a symbol block inserted at a point" |
| Arrows, rings, "?" and "A .55" tags at symbols; dashed lines and "lateral" / "main" text between hydrants and valves | **FTFORIENT** QC graphics, on the non-plotting layers `FTF-QC-ORIENT` and `FTF-QC-ORIENT-INFERRED` | `FTFWHAT`: "FTFORIENT: review graphics (not plotted ...)". `FTFORIENTRESET` removes them all |
| Survey figures, feature lines, linework | Civil 3D survey / the drafter | `FTFWHAT`: "Civil 3D: survey figure ..." or "not made by FieldToFinish" |

`FTFWHAT` with nothing selected counts the whole drawing by owner: COGO points, markers turned by
FTFORIENT, markers set by a drafter, and FTF objects by the command that made them. Every kind of
object FTF stamps is named there; a test fails the build if a new kind is added without a name.

## Root causes

### 1. "HYDRANT LATERAL (INFERRED)" and the inferred utility graphics

- **Made by:** FTFORIENT. When three valves near a hydrant form a tee, or two valves sit one on the
  main and one toward the hydrant, the valve pass infers a water main and a hydrant lateral.
- **What FTFORIENT drew for every one:** a line, plus the connection's kind in capitals as MText at
  the line's midpoint. It did this on every run, including the Process Drawing pipeline.
- **Why it was large:**
  - The text height was hard-coded (0.06 in plotted).
  - It went through the annotation-scale conversion in item 3 below, which can make every FTF
    annotation 12 times too big. In a feet drawing whose 1" = 20' scale is defined in inches
    (1:240), that 0.06 in text becomes 14.4 ft tall.
  - It was long, unrotated, and laid across whatever was there.
- **The underlying inference was weaker than it looked.** A two-valve arrangement was applied at
  0.6 confidence and drew a full lateral.

**Corrected:**

- Inferred connections are review items: kind "Probable underground connection (not verified)", Info,
  not required, zooming to the valves and hydrant.
- They are in the CSV as "inferred, not verified: never a deliverable".
- They are not drawn unless you run `FTFORIENT Preview`, or turn on "Draw inferred underground
  connections" in Settings. When drawn they are:
  - a dashed line on the non-plotting layer;
  - a short tag ("hydrant lateral?") along the line at the QC text height (a setting, 0.06 in
    plotted, converted correctly);
  - never sized from the length of what they mark.
- Two valves are not an assembly. Their probable arrangement is suggested (Inferred, 0.55) and not
  applied.
- The three-valve tee is unchanged.
- The hydrant's nozzle is never decided from its lateral.

### 2. Questionable rotations, and the QC arrows read as symbols

**Before**, FTFORIENT drew a QC arrow, starting at the symbol itself, for every decision. That
included decisions it was not sure enough to apply. In a dense drawing, those arrows and their
tags read as turned symbols pointing the wrong way.

**Now:**

- After Apply, QC is drawn only for what needs review.
- Each arrow starts beside the symbol.
- Preview still draws everything.

Separately, these decisions really were wrong or invented:

| | Before (7771085) | Now |
|---|---|---|
| Street light or power pole arm with nothing recorded | Turned toward the nearest road at 0.65 (applied) | Suggested toward the road and listed for review. The pavement does not show which way an arm points. The fallback for street lights and signal poles is Review; an office can set Apply |
| An edge with a small jog where the sign stands | The nearest segment's direction: 45 degrees off | The direction over 20 ft of line: right |
| A short driveway edge 3 ft from a sign, road edge 8 ft away | The driveway's direction: 90 degrees off | The road edge (long lines outrank short ones) |
| An edge that stops 40 ft before the sign | Its end segment used as if it ran on | Not used past 25 ft (a setting); the sign goes to review |
| A corner with two road edges and no centre lines | Whichever edge was nearer | Review: "road lines here disagree", with both suggestions |
| Two valves near a hydrant | Both turned (0.6) | Suggested, not turned (0.55) |

Fences, property lines and utility lines were never road references in FTFORIENT. It reads only
road, stripe, curb, stop-bar, crosswalk and utility layers and codes. So a rotation that follows
a fence was not made by FTFORIENT. Check it with `FTFWHAT`: it may be a description key or a
hand rotation.

### 3. Oversized annotation: plotted height to drawing units

Every FTF annotation height is a plotted height times "drawing units per plotted inch". That
was taken as the annotation scale's drawing units divided by its paper units, with no regard to
the drawing's units. A "1" = 20'" scale exists both as 1:20 (made for a drawing in feet, as Civil
3D's are) and as 1:240 (made the AutoCAD way, in inches). In a feet drawing the second made every
FTF label, leader, mask and QC mark twelve times too big. The old code comment even said a
1"=20' sheet returns 240.

**Corrected:** `FieldCodes.Geometry.PlotScale` reads an imperial scale by its name ("1" = 20'",
"1/4" = 1'-0"", "1 in = 20 ft") in the drawing's INSUNITS:

- 20 drawing units per plotted inch in a feet drawing, 240 in an inch drawing, whatever ratio the
  scale was defined with;
- a ratio scale (1:20) or a metric drawing keeps its ratio, as before.

All 25 annotation routines use it through `CadUtil.DrawingUnitsPerPlottedUnit`. Your example
(0.08 in at 1" = 20' in a feet drawing) is 1.6 ft, and a test holds it.

### 4. Overlapping labels

FTFLABELS already measures each label's real text extents, collects the symbols and labels
around it as obstacles, searches rings of positions, uses a leader where the style allows,
keeps labels moved by hand where they are, and never lays a label over a protected symbol.

- **What was missing:** a label with no clear position was dropped, with only a command-line line
  to say so.
- **Now:** each one is on the review list ("Label has no clear position"), zooming to its point.
- **Still the likely causes of overlap in the screenshots, all fixed above:**
  - the scale defect (labels sized twelve times too large overlap everything);
  - the always-drawn QC text;
  - inferred connection text.
- **Duplicate labels with Civil 3D's own:** unchanged and already handled. A point whose label
  style shows its description is not labelled again (the drafting standard's "point label says
  it").

## Settings (Symbols and Rotations > Rotation evidence and review)

All in the orientation section of the office's rules file, edited and checked like the rest of
the page:

| Setting | Built in |
|---|---|
| Look for road lines within | 80 ft |
| Follow a line that stops short at most | 25 ft |
| Take a line's direction over | 20 ft |
| Road lines disagree beyond | 15 degrees |
| Trust: centre lines (and one-way) / lane and fog lines / pavement edges, curbs, flowlines / edges of concrete | 1.0 / 0.9 / 0.85 / 0.5 |
| Signs and markings at an intersection | Decide from the approach (or Review) |
| Fallback per kind: signs, street lights, signal poles, hydrants, arrows, words, water and gas valves | Apply, except street lights and signal poles: Review |
| A marker turned by hand | Keep, unless the crew measured the direction (or Always keep) |
| QC graphics | Only what needs review (or Every decision, None) |
| Draw inferred underground connections | Off |
| Turn at confidence of at least / evidence at least as strong as | 0.6 / Inferred (unchanged) |

## Reviewing one rotation: FTFORIENTPOINT

The review list's Fix runs `FTFORIENTPOINT <point>`. It shows:

- the marker rotation now, what FTF set, and the rotation before FTF;
- the direction and the marker rotation it would set;
- the tier, confidence and source (recorded, related feature, road alignment, fallback rule);
- the reference line and its distance;
- every piece of evidence;
- whether FTFORIENT would turn it.

It then offers:

- **Zoom**
- **Preview**: a QC arrow; nothing is turned.
- **Apply**: take the suggestion.
- **Override**: an azimuth, or pick a point the symbol should point to.
- **Recheck**: hand it back to FTFORIENT and decide again.
- **Reset**: put it back as it was before FTF.

Apply and Override are kept by every later FTFORIENT, even `All`. The CSV now also lists the
reference, its distance, the source, the proposed rotation, whether it was applied, and whether
it needs review.

## Testing

- **Unit:**
  - `RoadReferenceTests`, 20 scenarios: no centre line, edges that stop short (within and past the
    limit), a gap, a curve, a jog, a fence nearer than the road, a driveway edge, disagreeing
    edges, the tolerance and search settings, written arrow directions on partial and curved
    roads, re-running gives the same rotation, and the settings reading from the rules file;
  - `PlotScaleTests`: 19 checks, including 0.08 in at 1" = 20';
  - `OrientationSettingsEditingTests`;
  - `OrientationReviewTests`;
  - the existing orientation, valve and review tests.
- **Changed on purpose, with the reason in each test:**
  - an unmeasured street-light arm is now reviewed, not turned; the old behaviour returns with the
    Apply fallback;
  - two valves near a hydrant are suggested, not turned;
  - the CSV has six more columns.
- **Before and after** in the table above are the same reconstructed scenes run through the engine
  at 7771085 and now. They are synthetic geometry, not the survey drawing.
- **Not verified in Civil 3D yet:**
  - FTFORIENTPOINT, FTFWHAT, the new QC drawing, the new settings tab;
  - the label review items;
  - the scale fix on a real drawing.

  The plugin compiles against the stand-ins and against the real AutoCAD 2024 API. The next step
  is to run them on the drawing in the screenshots.

## Limitations

- **The five screenshots were not seen.** Which of the causes above each one shows has to be
  confirmed in the drawing (`FTFWHAT`, the orientation CSV).
- **Road side and sense:** keep-right is assumed on two-way roads. A road drawn by its edges only
  may be one-way, so edge-only decisions stay below the apply threshold (0.55) unless something
  else (a stop bar, an approach, a written direction) decides them.
- **Divided roadways and medians** have no special model beyond what the centre lines and edges
  give. A sign in a median between two disagreeing lines goes to review.
- **The local tangent** is a chord over 20 ft of line: on very tight curves (radius under about
  50 ft) it differs from the true tangent by a few degrees.
- **A metric drawing** keeps its scale's ratio, as before. Imperial names in metric drawings are
  not reinterpreted.
- **Label overlap with Civil 3D's own labels:** FTFLABELS treats COGO points and their labels as
  obstacles. It cannot move Civil 3D's labels, and never moves a label placed by hand.
