# TRACE Pilot Blockers (as of 2026-11-05)

Ordered by what stops progress first.

1. **Ownership and reuse rights not cleared.** No code may be copied into a commercial repository, and nothing may be sold, until this is confirmed in writing.
2. **No Windows/Civil 3D 2024 environment available to this assistant.** The current session is Linux with no .NET SDK or Civil 3D, and no user computer is connected via Remote Control. Build, test and CAD verification cannot be performed until a Windows machine is connected or a developer runs the checklist locally. This is the main schedule risk.
3. **CAD behaviour of the core commands is unverified.** Code headers still carry UNTESTED notices and the README "Needs verifying" list is open. Only the drafting-lines workflow has a documented headless run.
4. **Parametrix standards embedded in factory defaults.** A customer would receive Parametrix layers, codes and labels. Must be replaced by a neutral default profile before any external install.
5. **Installer is dev-only.** `install-dev.ps1` and a bundle manifest only; no proper installer, uninstall, or version handling. Build hard-codes `C:\Program Files\Autodesk\AutoCAD 2024`.
6. **No licensing or trial mechanism.** Needed before a paid or trial pilot (signed license file design is in the checklist).
7. **Technician interface unproven.** Ribbon, main window and settings pages have never been shown in Civil 3D; they also expose all legacy commands, including excluded modules.
8. **Modifications to existing Civil 3D objects (e.g. marker rotation) are not reversible.** Known open requirement; either remove from the pilot or implement prior-value recording.
9. **Trademark/name check for TRACE and company/legal setup for selling** (EULA, invoicing, support) not yet done.
10. **No non-confidential sample drawings.** Needed for testing, the sample project and documentation; Parametrix drawings must not be used.
