# Can TRACE and FieldToFinish run side by side?

**Short answer: the files can sit side by side, but the two editions must not be active in the same Civil 3D session.** Static review (no Civil 3D available when this was written) found these conflicts.

| Area | Before | Now |
|---|---|---|
| Bundle folder, manifest, product and upgrade IDs | TRACE already had its own | unchanged: `TRACE.bundle`, own `PackageContents.xml`, own IDs |
| Assembly identity | **Same names** (`FieldCodes.dll`, `FieldCodes.Cad.dll`). Civil 3D would load only one and silently reuse it for the other | **Fixed.** TRACE edition builds `TRACE.Core.dll` and `TRACE.Cad.dll`. Internal edition keeps its names exactly |
| Files copied into a bundle | Whatever was in the build folder, including leftovers from the other edition | **Fixed.** The installer never copies the other edition's files |
| Command names | Both register the same `FTF*` commands | **Not fixed.** Civil 3D's behaviour for duplicate command names across assemblies cannot be verified here, so it is treated as unsafe |
| Ribbon tab, XData names | Same tab id (`FTF_RIBBON_TAB`) and same ownership XData name | Same. Sharing the XData name is intended (a drawing finished by either edition is understood by the other). The duplicate tab id means the second ribbon is skipped |
| Auto-loading | Both load at startup | Same. Both would load |

So the commands cannot be guaranteed safe together. Until TRACE gets its own command names (planned with the `TRACE*` aliases), use the reversible procedure below.

## Test TRACE without changing your FieldToFinish installation

Nothing inside `FieldToFinish.bundle` is ever edited, copied or deleted. The only action is renaming the folder, and the script proves afterwards that every file is identical.

1. Close Civil 3D.
2. Set it aside (renames `FieldToFinish.bundle` to `FieldToFinish.bundle.paused`; Civil 3D only loads folders ending in `.bundle`):
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\deploy\Switch-TraceTestMode.ps1 -Begin
   ```
   It fingerprints every file first (SHA-256) and saves the list under `logs\`.
3. Start Civil 3D and test TRACE (`docs\trace\MANUAL-VERIFICATION.md`). Close Civil 3D.
4. Put it back and verify:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\deploy\Switch-TraceTestMode.ps1 -End
   ```
   It must print `IDENTICAL: all N files match`. If it prints differences, stop and send me the output.
5. Check state at any time with `-Status`.

Safety rules built into the script: it refuses to run while Civil 3D is open; it refuses to rename over an existing folder; it never copies, deletes or writes inside the bundle.

If you would rather not rename anything at all, test TRACE under a different Windows user account or on another machine; then the original is untouched by construction.

## What the baseline script now guarantees
- TRACE is only ever installed to `...\ApplicationPlugins\TRACE.bundle`, and only after the destination is verified (name ends in `TRACE.bundle`, is not a FieldToFinish folder, and if it already exists its manifest is named `TRACE`).
- Any existing `FieldToFinish.bundle` is fingerprinted before the build and compared after the install; the run reports `untouched` or `CHANGED`.
- It does not stop because a FieldToFinish bundle exists. It installs TRACE next to it, reports that isolation is needed before running Civil 3D, and leaves the original alone.
