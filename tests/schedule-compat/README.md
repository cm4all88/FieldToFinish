# Schedule compatibility check

Crew Upload reads the Survey Schedule's files with `src/CrewUpload.Schedule/ScheduleAssembler.cs`.
That file is a **C# port of the Schedule's `assembleState()`**: the same merge rules (PM feeds in
`master.pms` order, requests appended, newest override per id, owner's newer edit wins), written a
second time. A port can drift from the original, so it is checked against the original:

- `assembleState.reference.js` is the Schedule's own `emptyFeed()` and `assembleState()`, extracted
  character for character from the Schedule HTML by `extract.js` (its SHA-256 is in the header and is
  checked by the tests, so it cannot be hand-edited unnoticed).
- `harness.js` runs that code on a schedule folder, reading the files as `readFolderState()` does.
- `compat.js` compares its projects and entries with what Crew Upload's reader produced
  (`ScheduleCheck <folder> --assembled out.json`). Only JSON-writing differences are ignored
  (C# leaves out nulls; a missing `withIds` is `[]`).
- `CompatibilityTests.cs` runs this on hand-made folders and 300 generated ones (ties, missing
  timestamps, numeric ids, pending requests, missing and broken feeds) on every test run where
  Node.js is installed.

**When the Schedule changes**, against its new HTML:

```
node tests/schedule-compat/extract.js "Survey Schedule PSO.html"            # sha256 differs => assembleState() changed
node tests/schedule-compat/extract.js "Survey Schedule PSO.html" --write    # take the new code as the reference
dotnet test tests/CrewUpload.Schedule.Tests                                 # fails if Crew Upload now reads differently
```

**Against real data** (read-only; a copied folder is just as good):

```
ScheduleCheck.exe "\\parametrix.com\pmx\PSO\Shared\Divisions\00Survey\OFC_RSC\Schedule" --assembled crewupload.json
node tests/schedule-compat/compat.js --html "Survey Schedule PSO.html" --folder "<same folder>" --csharp crewupload.json
```

What this does not cover, on purpose: `master.patterns` (generated 9-80 Fridays, 4-10s) and holidays,
which Crew Upload does not use; day-by-day coverage, which is `start <= day <= end` exactly as the
board draws it; and crew grouping, which is Crew Upload's own reading (same project, same day, field
day types) -- `ScheduleCheck` prints any entry whose `withIds` disagrees with it.
