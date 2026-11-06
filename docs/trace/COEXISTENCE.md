# TRACE and FieldToFinish side by side

TRACE is independent of FieldToFinish and runs beside it. Nothing needs to be set aside.

| Area | FieldToFinish (internal, unchanged) | TRACE |
|---|---|---|
| Bundle | `FieldToFinish.bundle` | `TRACE.bundle` (own manifest and IDs) |
| Assemblies | `FieldCodes.dll`, `FieldCodes.Cad.dll` | `TRACE.Core.dll`, `TRACE.Cad.dll` |
| Commands | `FTF...` | `TRACE...` (no `FTF` name registered) |
| Data stored in drawings | `FTF_FIELDTOFINISH` | `TRACE_OBJECTS` (neither edition reads or erases the other's objects) |
| Settings folder | `%APPDATA%\FieldToFinish` | `%APPDATA%\TRACE` |
| Files beside drawings | `.ftf-*` | `.trace-*` |
| Ribbon tab, palette | FTF tab | TRACE tab and palette |

Guarded by tests (`RuntimeIndependenceTests`): the internal edition still registers exactly its original 64 command names; TRACE shares none; stored-data and file names differ.

`deploy/Switch-TraceTestMode.ps1` and the baseline script's fingerprint check were for the earlier shared-command design and are no longer needed.
