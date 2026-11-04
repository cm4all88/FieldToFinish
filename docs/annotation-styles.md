# Annotation styles

How FTF decides what its labels, tags and line labels look like, and how to change it.

## In one paragraph

Every label FTF draws gets its look from one place, `StyleResolver`. It starts from the
values the existing pages have always held (**Point Labels** for point labels, **Tags** for
tags, **Line Labels** for line labels), then applies **Style Assignments** from the least
specific (every feature) to the most specific (one field code). An assignment can name a
**Text Style** and a **Leader Style**, and can set the layer, offset, leader use and whether
the feature is labelled at all. A named style only changes the fields it fills in; a blank
field inherits. With no styles defined, FTF draws exactly what it drew before.

## The pages

| Page | What it holds |
|---|---|
| Settings > Text Styles | Named text looks: drawing text style, plotted height, width factor, oblique angle, rotation (horizontal, fixed, follow the symbol), justification, colour, mask (wipeout, background mask, none) and wipeout layer, prefix, suffix, decimal places, upper case, annotative. |
| Settings > Leader Styles | Named multileader looks: multileader style, straight or spline, leader colour, linetype and lineweight, arrowhead (AutoCAD's arrows such as `_Dot`, `_DotSmall`, `_Open30`, `_ArchTick`, `_None`, or a block in the drawing) and plotted size, landing on or off, landing length, gap to text, text attachment, maximum length, annotative. |
| Settings > Style Assignments | One row per scope: for point labels, tags or line labels; for every feature, a family (Trees, Utilities, Signs...), a rule, or one field code. A tester under the table resolves a description typed as the crew would ("FH 12") and lists every value the label will get **and where it came from**, beside a preview. It also checks every named style, multileader style, arrow block and linetype against the drawing. |
| Settings > Point Features | Each feature's screen has an **Appearance** section: label this feature on or off, its label text style, leader style and offset, its tag text style, the effective values with their origins, and a preview. These are rule-level assignments, the same rows Style Assignments shows. |

All four pages edit one working copy, so a style added on Text Styles appears at once in
the Point Features and Style Assignments lists. The window's **Save** writes them (the Point
Features page's **Save Rules** saves the rules file, which is separate).

Each preview is drawn at plot scale on a dark, model-space-like background, with a line running
behind the text so the mask shows. The font is the text style's TrueType face when Windows has
it; an SHX font is shown with a stand-in, and the caption says so.

## Which value wins

For one label, field by field:

1. the most specific matching assignment that sets it (code, then rule, then family, then
   every feature);
2. inside a named style, a filled-in field; a blank field falls through to
3. Point Labels / Tags / Line Labels (text style, height, mask, offset, arrow size);
4. for leader fields, the multileader style named (or the drawing's current one);
5. FTF's built-in default (width 1, horizontal, middle-of-text attachment).

Example: family **Utilities** uses text style *Utility* (0.10", cyan) and leader style *Dot*;
code **FH** uses text style *Hydrant* (prefix "FH ") and leader **Always**. A hydrant gets
*Hydrant*'s prefix, Point Labels' height (because *Hydrant* does not set one -- the most
specific named style is used, not merged with the family's), the *Dot* leader from the family,
and always a leader. The Style Assignments tester shows exactly this, line by line.

## What each setting changes in the drawing

FTF leaders are AutoCAD **multileaders** (MLEADER), so every leader setting is a real
multileader property: `MLeaderStyle`, `LeaderLineType`, `LeaderLineColor`, `LeaderLineTypeId`,
`LeaderLineWeight`, `ArrowSymbolId`, `ArrowSize`, `EnableLanding`/`EnableDogleg`,
`DoglegLength`, `LandingGap`, `TextAttachmentType` (both sides), `TextAlignmentType`,
`TextColor`, `Annotative`. A blank field is not touched, so the multileader style decides.

Label text is MTEXT: text style, height, rotation, attachment, colour and background mask are
MTEXT properties; width factor and oblique angle are written as MTEXT codes (`\W0.8;`, `\Q15;`)
on every line, the way AutoCAD's own editor writes them.

**Civil 3D point label styles are not multileaders.** The label a COGO point draws through
its own point label style (Civil 3D's Settings tab > Point > Label Styles) is Civil 3D's, and
FTF does not pretend a multileader setting applies to it. FTF's styles control the labels,
tags and line labels FTF itself draws.

Decimal places round numbers written with a decimal point (6.418 to 6.42 at two places,
6.4 to 6.40); whole numbers -- pole, sign and tag numbers -- are never changed. A
`{field:0.0}` format in a rule's label template still rounds that field first.

Maximum leader length limits how far out FTF's label search looks: positions beyond it are
not tried, and a label with no position inside it is reported as having no clear position.

## Missing styles are reported, never swapped silently

Before drawing, each command checks every text style, multileader style, arrowhead block and
linetype the settings name. At the end of FTFLABELS, FTFTAGS, FTFLINELABELS and FTFRESTYLE,
anything missing is listed by name with where it is set and what was used instead, e.g.

    FTFLABELS: 1 style problem(s) -- the labels concerned used the values shown in brackets ...
      Text style "L80" is not in this drawing [the drawing's current text style].

An assignment that names a style that is not defined is reported the same way, and the
Settings pages show the same problems in red while you edit.

## Existing drawings: FTFRESTYLE

New labels pick up the styles on their own. For annotation already on the sheet:

    FTFRESTYLE  [Preview/Apply] <Preview>

* **Preview** lists, per kind, how many FTF labels, tags and line labels there are and what
  would change ("text height 1.6 to 2 x37", "layer V-TEXT to V-UTIL-TEXT x12"). Nothing changes.
* **Apply** makes those changes in place.

It only touches entities FTF drew and still owns: the FTF stamp, on the entity it was written
on. A copy someone made, text someone typed, and every other entity are never touched.
Nothing is moved -- a label someone dragged stays where it was put and a leader keeps its
vertices. Wording someone edited is kept (only the look is updated): FTF now records a hash of
the text it wrote, and text that no longer matches is someone's edit. Labels drawn before this
build have no hash; their wording is re-formatted only when it is still exactly what FTF wrote.

## Where it is stored

`ftf-settings.json`, section `"styles"`:

```json
"styles": {
  "text": [
    { "name": "Utility", "textStyle": "L80", "heightPlotted": 0.1, "color": "4", "mask": "Background" }
  ],
  "leaders": [
    { "name": "Dot", "arrowBlock": "_Dot", "arrowSizePlotted": 0.05, "lineType": "Spline", "landing": false }
  ],
  "assignments": [
    { "kind": "Label", "category": "Utilities", "textStyle": "Utility", "leaderStyle": "Dot" },
    { "kind": "Label", "code": "FH", "leader": "Always" },
    { "kind": "Label", "code": "MB", "enabled": false },
    { "kind": "Tag", "category": "Trees", "textStyle": "Utility" }
  ]
}
```

Empty fields are not written. Each FTF label also records, in its stamp, the style it was drawn
with (`text style|leader style|mask`), the text it was formatted from, and a hash of what was
written.

## Checked in Civil 3D on every deploy

`deploy\smoke-test.ps1` (run by UPDATEFTF) does a separate headless run on a fresh drawing with the
build's own rules: it seeds five COGO points and runs FTFLABELS under test styles. It then checks
the real MTEXT and MLEADER properties:

- text style, height, colour, prefix, background mask and layer;
- spline leader, `_Dot` arrow, no landing;
- a feature turned off.

The hydrant FH is labelled by an office drafting standard the check writes beside the drawing (the
built-in standard gives a fire hydrant no text label). WFH 13 keeps the built-in standard, so its
"leader Always" assignment must draw nothing: a leader belongs to a label.

FTFRESTYLE Preview must change nothing. Apply must land the new look and move nothing: the
hydrant's text and arrow stay where FTFLABELS put them, and a hand-moved label stays where it was
put.

It is a **required** check, like identity and accountability: a failure makes UPDATEFTF put the
previous build back. The log is `StyleTestHarness\StyleTestHarness.log` in the smoke folder.

At e979c7f two things failed:

- The hydrant had no label at all (the drafting standard, not the styles).
- A hand-moved pole label, a multileader, came back moved after Apply: re-styling a multileader's
  text height and attachment makes AutoCAD re-seat the text against its landing. FTFRESTYLE now
  puts the text and every leader's arrow back where they were after applying the look.

## Code

| Where | What |
|---|---|
| `src/FieldCodes/Styles/StyleSettings.cs` | The section, named styles and assignments, validation. |
| `src/FieldCodes/Styles/StyleResolver.cs` | Resolution with origins; `RingsWithin` for maximum length. |
| `src/FieldCodes/Styles/StyleText.cs` | Prefix, suffix, rounding, upper case, width and oblique codes, readable angles. |
| `src/FieldCodes/Styles/StyleCheck.cs` | Resources a drawing must have; AutoCAD's arrow names. |
| `src/FieldCodes/Styles/RestyleReport.cs` | What FTFRESTYLE found and changes. |
| `src/FieldCodes.Cad/AnnotationStyler.cs` | Applies a resolved style to MTEXT and MLEADER; resource lookups; problem list. |
| `src/FieldCodes.Cad/RestyleCommand.cs` | FTFRESTYLE. |
| `src/FieldCodes.Cad/Setup/StylePages.cs`, `StylePreview.cs` | The three pages and the preview. |
| `tests/FieldCodes.Tests/StyleResolverTests.cs` | Unit tests. |
| `tests/LiveSmoke/StyleTestHarness.cs` | The Civil 3D check. |
