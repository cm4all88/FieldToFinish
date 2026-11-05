# TRACE UI and branding: approved decisions (2026-11-05)

Source mockups: `docs/trace/design/` (HTML boards and PNG previews). Mockups only: nothing below is implemented yet.

## Gate
**No UI implementation until the Windows and Civil 3D baseline is verified** (`deploy/Run-TraceBaseline.ps1` clean and `docs/trace/MANUAL-VERIFICATION.md` completed). After it passes:
1. **Stage 1: identity and ribbon.** Focused tests, then Civil 3D check.
2. **Stage 2: dockable palette.** Focused tests, then Civil 3D check.
Later stages (Profiles and Standards window, Review Queue, Start / About) follow the same rule: one stage, focused tests, verify in Civil 3D.

## Identity
- Name: **TRACE**. Full name always available: **Topographic Reconstruction and Automated CAD Engine** (Start window, About, installer, documentation; never beside ribbon buttons).
- Product description: **Survey Drafting Automation for Civil 3D**.
- Logo: mountain ridge with survey target and green swoosh (vector redraw; trademark search still to do). No Autodesk logo or colours; footnote "Civil 3D is a trademark of Autodesk, Inc."
- Palette: TRACE Navy `#0B2E4F`, Survey Blue `#1E6FB8`, Accent Green `#7CC84A`, Light Gray `#E6EBF0`; attention amber and status colours per the brand board. Status is never colour alone.
- Interface font Segoe UI; wordmark shipped as artwork.

## Behaviour requirements
- **Run TRACE** is the main workflow button.
  - The technician chooses which operations run (clean, trees, labels, draw order, and so on).
  - The selection is remembered per user and applied next time.
  - A run never overwrites manual drafting changes: technician edits are detected and kept (builds on the existing ownership XData and moved-label rule). Anything TRACE cannot safely change is reported in the Review Queue, not overwritten.
- **Palette**
  - Resizable, collapsible and remembers its position and size.
  - Not forced open at Civil 3D startup; the TRACE ribbon button opens it.
- **Ribbon, Profiles and Standards, Review Queue:** approved as mocked (Review Queue keeps the Measured / Inferred basis).
- **Hidden in TRACE 1.0:** Production, Sheets, easement, legal description, exhibit, DIP and utility tools. In this repository they are hidden by edition: the TRACE edition builds no ribbon entries and no palette entries for them. The internal Parametrix edition keeps everything exactly as today.
- **No licensing or trial status shown anywhere** until licensing exists. The mockups have been updated accordingly (About shows version and edition; palette footer shows version).
- **Commands:** every existing `FTF*` command keeps working. TRACE adds `TRACE*` aliases that call the same code.

## Technical notes for the later stages
- Ribbon: extend the existing builder in `FtfRibbon.cs` (edition-aware). Reports and Help are new thin commands (open existing CSV reports; open documentation).
- Palette: Autodesk `PaletteSet` hosting a new control that calls the existing pipeline and status services. Persist run selections and palette state in the TRACE settings folder (`%APPDATA%\TRACE`). Must be verified in Civil 3D 2024: docking, DPI scaling, keyboard focus while a command prompts.
- Edition gating reuses the `TraceEdition` build property and the `Branding` stamp already in place.
