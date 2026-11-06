# TRACE Commercial Preparation: Implementation Checklist

Status: planning only. No source code has been changed or copied. This branch (`claude/sleepy-allen-e5i3ru`) holds planning documents inside the existing FieldToFinish repository; the Parametrix production version, history and working builds are untouched.

TRACE = Topographic Reconstruction and Automated CAD Engine. A Civil 3D production-automation tool used after survey point processing and initial linework are complete.

## Approved decisions (2026-11-05)
1. Scope: existing CAD production commands, labeling, annotation, cleanup, tags, tables, trees, QC.
2. Excluded from 1.0: easements, legal descriptions, exhibits, sheets/key maps, DIP/utility.
3. Civil 3D 2024 first; other versions after successful testing.
4. No Parametrix profiles, standards, paths, or project data in the commercial product.
5. Price target $1,500/seat/year; 14-day trial.
6. Preserve FTF commands; add TRACE aliases where appropriate.
7. Separate private commercial repository, only after ownership/reuse rights are cleared.
8. Preserve all existing repositories, history and working versions.

## Gate 0: Ownership (superseded 2026-11-05)
Owner states TRACE predates Parametrix employment; remaining legal questions handled separately before distribution. Not a development blocker. Keep Parametrix standards/confidential data out of the commercial product.
- [ ] Owner written determination of who owns the FTF code (employer agreements, contributors, third-party code).
- [ ] Confirm Newtonsoft.Json (MIT) and any other dependencies are redistributable; list in a third-party notices file.
- [ ] Decide which files are cleared for reuse; record as a manifest (path, status, reviewer, date).
- [ ] Only then: create private repo `TRACE` (name TBC; trademark check pending) and import the cleared manifest.

## Phase 1: Windows baseline on Civil 3D 2024
- [ ] Windows machine with Civil 3D 2024 and .NET SDK; clone this repo.
- [ ] `dotnet build FieldToFinish.sln`; record result and warnings.
- [ ] Run the non-CAD unit tests once (`tests/FieldCodes.Tests`) as a baseline; record pass/fail count. Do not rerun the full suite for minor changes afterwards.
- [ ] Install via `deploy/install-dev.ps1`; confirm plugin loads, ribbon appears, `FTFWHERE` reports.
- [ ] Run existing headless harness `tests/LiveSmoke/run-livesmoke.ps1` and cleanup harness; archive logs as baseline evidence.
- [ ] Open 2-3 representative non-confidential drawings (own sample data) for manual verification.
- [ ] Record Civil 3D 2024 exact build/update number and the Newtonsoft.Json version next to acad.exe.

## Phase 2: Separate customer configuration from company standards
- [ ] Inventory every Parametrix-specific item (list in "Standards inventory" below) with file/line.
- [ ] Define a profile schema: layers/colors/linetypes/lineweights, text styles, label layers, feature/line definitions, tree grammar, symbol labels, protected/maskable layers, units.

- [ ] Profile import/export in the setup UI; validation messages; `.bak` on save (already present).
- [ ] Replace hard-coded `V-…` layer derivation in `LabelLayerResolver`, `ProductionLayerResolver`, `LayerClassifier`, `SpotCommands`, `Pages` with profile-driven values.
- [ ] Move `lineworkCodes`, `controlCodes`, `symbolLabels`, `lineFeatures` out of factory `rules.json` into default profile content.
- [ ] Keep the Parametrix profile outside the commercial repo (private, loaded as a customer profile in testing only).
- [ ] Focused tests: profile load/validate/round-trip; resolver tests with a non-Parametrix layer set.

## Phase 3: Verify core commands on real drawings (see pilot five below)
For each: scripted or documented manual test, expected result, observed result, defects logged with severity.
- [ ] Re-run safety: run twice, entity count identical.
- [ ] Technician edits preserved (moved labels, edited text).
- [ ] Undo behaviour (single UNDO reverses a command).
- [ ] Failure behaviour: bad input produces a clear message and no partial corruption.
- [ ] Fix critical defects only; record the rest.

## Phase 4: Installer and technician interface
- [ ] `.bundle` + per-machine installer (Inno Setup or WiX) that writes only inside the bundle and installs/removes cleanly.
- [ ] Uninstall removes bundle and leaves user config in `%APPDATA%` (documented choice).
- [ ] Ribbon/main window trimmed to the pilot commands; TRACE branding, TRACE-named aliases.
- [ ] Results summary after a run in plain language; "copy diagnostics" button.
- [ ] Settings stored in `%APPDATA%\TRACE`; one-time migration path from `%APPDATA%\FieldToFinish` for existing FTF users.

## Phase 5: Basic licensing and pilot
- [ ] Signed license file (Ed25519 public key embedded), per-seat, annual expiry; 14-day trial via same mechanism.
- [ ] Clear "trial / expired" messaging; commands refuse politely, never damage a drawing.
- [ ] Offline verification; optional lightweight activation later.
- [ ] Pilot package: installer, quick-start (2-4 pages), sample drawing, feedback form, diagnostics bundle.
- [ ] 2-4 pilot firms on trial licences.

## Command aliases (proposal, to be applied in Phase 4)
Keep every `FTF*` command. Add `TRACE*` aliases for pilot commands, e.g. `TRACE` (window), `TRACECLEAN`, `TRACELABELS`, `TRACETREES`, `TRACEDRIP`, `TRACELABELLINE`, `TRACEDRAWLINE`, `TRACESETUP`. Aliases must call the same methods (no duplicated logic).

## First five workflows for pilot verification
Chosen by: value to technicians, reliance on already-tested logic, existing live-test evidence, and gating importance.

1. **FTFCLEAN plus re-run safety (ownership XData).** Gates every other command; has a cleanup harness; premise of the whole design is unverified in interactive Civil 3D.
2. **FTFLABELS** (point labels with collision avoidance, masks, leaders, hand-moved label preservation) and **FTFORDER** as its finishing step. Strong unit tests; CAD half "never run" per README; label box size and wipeout masks need on-screen checks.
3. **FTFTREES then FTFDRIP** (tree labels and trimmed driplines). Most distinctive production time-saver; needs neutral tree grammar and a real point group; arc direction must be visually confirmed.
4. **FTFLABELLINE** (interactive line labelling) with the **FTFLINES** inventory/review. High-value, "never guess" behaviour; depends on the label-layer resolver (standards separation) and real linework.
5. **FTFDRAWLINE and FTFDRAFTCLEAN.** Closest to pilot-ready: the only workflow with a documented headless Civil 3D run (2026-08-20); remaining risk is the settings page UI, wipeout print behaviour and on-screen pick UX.

Next in line (not in the first five): FTFTAGS/FTFTABLE, FTFSPOT, FTFCONTROL, FTFLEGEND, FTFBLOCKS, FTFSETUP polish.

## Standards inventory (Parametrix-specific items to separate; paths approximate)
- `config/rules.json`: lineworkCodes (generated from Parametrix figure database), lineworkControlCodes notes, controlCodes, symbolLabels, lineFeatures, protectedLayers, maskableLayers.
- `config/profiles/PMX SURVEY EXHIBIT*.json` (exhibit-related; excluded from 1.0 and kept private).
- `src/FieldCodes/Linework/LabelLayerResolver.cs`, `Settings/ProductionLayerResolver.cs`, `LayerClassifier.cs`, `Settings/Sections.cs`, `Settings/FtfSettings.cs`, `Editing/FeaturePreview.cs`, `Linework/LineworkReview.cs`: `V-…` layer patterns and PMX/Parametrix text.
- `src/FieldCodes.Cad/Setup/Pages.cs`, `SpotCommands.cs`: layer defaults.
- `deploy/FieldToFinish.bundle/PackageContents.xml`: vendor/company strings.
- `tests/RealWorld/*` (OFFICE-STANDARD.md, cases/, scripts with `U:\`, `C:\PMX`, `C:\dev`): proprietary project data; never copied.
- `checkpoints/*/build-net48/rules.json`: historic builds; keep in evidence repo only.
- Excluded modules (code stays in the evidence repo, not copied): Easement*, Exhibit*, Sheet*, Utility*, Dip*, LegalDescription, Closure, Composition, CurveChain, StripTrim, Ties, CourseGeometry, StandardsView, and their Settings and tests.

## Progress log
- 2026-11-05: Non-CAD baseline 943/947, then 968/968 after fixes (see BASELINE-NONCAD.md).
- 2026-11-05: Neutral `config/default/rules.json`; Parametrix files moved to `config/internal/parametrix`; `TraceEdition` build switch; CSV standards importer with tests; 4 path tests made portable; TRACE bundle manifest, release-guard build script and Inno installer script (all UNTESTED on Windows). See CONFIGURATION.md.
- 2026-11-05: Settings folder is edition-aware: TRACE edition uses `%APPDATA%\TRACE`, internal keeps `%APPDATA%\FieldToFinish`; no migration (TRACE starts from neutral defaults). Assembly rename plan written (ASSEMBLY-RENAME-PLAN.md); Windows build/test steps written (WINDOWS-STEPS.md). 978 non-CAD tests pass.
- 2026-11-05: Location-independent Windows scripts (`deploy/TraceEnv.ps1`; LiveSmoke scripts generate their .scr from the checked-in ones). One-run `deploy/Run-TraceBaseline.ps1` (prereqs, build TRACE, unit tests, install, logs + zip). `docs/trace/MANUAL-VERIFICATION.md` with a synthetic kit in `tests/Manual/` (kit outcomes pinned by ManualKitTests). All UNTESTED on Windows. Internal Parametrix assembly not renamed. 981 non-CAD tests pass. Holding all feature, licensing and installer work until the Civil 3D baseline is confirmed.
- 2026-11-05: UI and branding direction approved (see UI-DECISIONS.md and docs/trace/design). Implementation is gated on the verified Windows and Civil 3D baseline; then identity and ribbon, then palette, focused tests after each stage.
- 2026-11-05: Coexistence correction. Distinct assembly identities (TRACE.Core.dll / TRACE.Cad.dll; internal names unchanged), installer and baseline verify the destination before copying and prove an existing FieldToFinish.bundle is untouched, never copy the other edition's files. New reversible `Switch-TraceTestMode.ps1` (rename only, SHA-256 proof). Duplicate FTF* command names remain; see COEXISTENCE.md. 991 non-CAD tests pass.
- 2026-11-06: TRACE ribbon tab, dockable palette (Run TRACE with remembered step selection), TRACE/TRACERUN commands built, TRACE edition only (`TRACE_EDITION`); internal edition source paths unchanged. Shared button catalog and icon set in the core library with focused tests (every button runs an existing command; no FTF text visible). Hidden in TRACE settings window: Sheets, Dips, Easements, Exhibits. Needs first Civil 3D check: docs/trace/INTERFACE-CHECK.md.
- 2026-11-06: Branding pass on the existing windows and output (TRACE edition only): command-line messages go through a wording filter (396 Editor calls now use FtfText.Tell; internal edition passes text through unchanged); Settings window and main window restyled with the approved palette and Segoe UI; captions and "Affects:" lines re-worded. Wording logic in core (TraceWording) with tests.
