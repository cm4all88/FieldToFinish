# TRACE workspace: one integrated application (proposed, not built)

Requirement (2026-11-06): TRACE must feel designed as one system, not old commands in a window. Keep the drafting engine; redesign how people use it. Mockup: `docs/trace/design/9-unified-workspace.png`.

## What the technician sees
- **Ribbon: six buttons** (Run TRACE, Workspace, Label line, Draw line, Review, Setup). Nothing else.
- **Workspace (the palette) is the application.** Top to bottom: standards in use; what TRACE found in this drawing (points, recognised features, codes needing a rule, lines); five stages in work order; the Run TRACE bar; Needs attention; a collapsed "More tools".
- **Stages:** 1 Read the drawing, 2 Points & trees, 3 Linework, 4 Draft, 5 Tables. Each has an on/off switch (remembered), a one-line status, and a drop-down for its own options inside the palette. No separate dialogs.
- **Run TRACE** shows a plan before it runs ("will finish 19 features, label 41 lines, build the schedule; your 3 edits are kept"), runs the chosen stages together with one progress bar, then reports in the same place.
- **Needs attention** is the single review list. Each item has its fix on the line (Add rule, Assign, Show, View). The same list feeds Run, the Review button and the status line.
- **Advanced** (clean up, draw order, spot elevations, legend, switch profile, diagnostics) sits under "More tools".

## How it is built on the existing engine
| Piece | Reuses | New |
|---|---|---|
| Drawing context ("what TRACE found") | existing status and review services (point reading, feature review, linework inventory) | one `TraceContext` read once per drawing and refreshed after each run; every stage reads it |
| Run plan | the existing pipeline steps and their order | a plan builder that turns context and ticks into the sentence shown, skips pointless steps, and runs Read first |
| Results | existing step outcomes, exception and unhandled reports, line inventory | one results model feeding progress, Needs attention and the reports |
| Stage options | existing settings pages and `FtfSettings` | each page hosted inline in its stage instead of a separate window; one settings object shared by all stages |
| Standards | existing profiles, rules and the CSV importer | profile chooser in the header; Add rule and Assign open inline |
| Draft | existing line drafting | inline form in stage 4 (start, bearing, distance); picks only where Civil 3D needs them |
| Prompts | existing behaviour | defaults come from standards; a prompt appears only when a choice is genuinely needed |

## Build order (each stage usable on its own)
1. **Workspace shell:** header, context facts, stages with switches, Run bar with plan and progress, Needs attention (read-only). Ribbon trimmed to six.
2. **Inline fixes and options:** Add rule, Assign, per-stage options hosted inline.
3. **Inline Draft** and the "More tools" section.
4. **Polish:** empty states, errors, high-DPI, keyboard.

Estimate for one developer: stage 1 about 1.5 to 2 weeks, stages 2 to 4 about 3 weeks. The engine is not rewritten.
