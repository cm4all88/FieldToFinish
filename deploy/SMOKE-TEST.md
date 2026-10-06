# Crew Upload + Survey Schedule: Windows smoke test

Run on a Parametrix Windows PC on the network, as a PM (the test registers a project and edits the
crew list -- in a **test** copy, not the real ones).

## Safety

- The Schedule folder is **only read**. Crew Upload and `ScheduleCheck.exe` open its files read-only.
  The script fingerprints every schedule file (SHA-256, size, time) before and after and reports any
  change. A change could only be a PM saving the schedule during the test.
- `scheduleReportStatus` is forced **off** in every test config, so `pso-progress.json` is not written.
- The project list, crew list and Survey folder used are test copies under
  `Documents\CrewUploadSmokeTest`. The real project registry and real project folders are not touched.
- Admin copies go to `...\FLD\CrewUpload\DailyReports-SmokeTest`, not the real `DailyReports` folder,
  so the real admin view is not filled with test reports. Delete that folder after the test.

## Run it

1. Build on Windows: `dotnet build src\CrewUpload.App -c Release`. Use
   `src\CrewUpload.App\bin\Release\net48\`; it holds `CrewUpload.exe`, `CrewUpload.Schedule.dll`,
   `ScheduleCheck.exe` and `job-folders.json`.
2. Pick a real project you are scheduled on **today**, and a few dates to compare (below).
3. Run:

   ```
   powershell -ExecutionPolicy Bypass -File deploy\schedule-smoke-test.ps1 -AppDir <build folder> -ProjectNumber 554-1800-119 -Dates 2026-07-13,2026-07-14,2026-07-17
   ```

   The script starts Crew Upload in each configuration, tells you what to check, and asks pass, fail
   or skip for each. It checks the files itself where it can (the PDFs, the record, the outbox, the
   upload, and that the schedule files are unchanged).
4. Send back `Documents\CrewUploadSmokeTest\smoke-results.txt` and `schedule-check.txt`.

| # | Check | How |
|---|---|---|
| 1 | Starts with the Schedule integration ON | the script launches it |
| 2 | Starts with the integration OFF | config `integration-off.json` |
| 3 | Starts with `CrewUpload.Schedule.dll` removed | copy without the DLL |
| 4 | Real Schedule files read from the share | `schedule-check.txt` |
| 5 | TODAY shows today's assignment | main window |
| 6 | Crew grouping is right | `schedule-check.txt` crews vs the board |
| 7 | Project linking shows the right Schedule project | Project setup, Link... |
| 8 | Schedule person shows names, not ids | Crew & work types |
| 9 | Daily Report opens from the TODAY card | |
| 10 | Schedule data prefills correctly | work type only where clearly mapped |
| 11 | Every prefilled field is editable | |
| 12 | "Not listed / Enter manually" works | |
| 13 | Report submits | |
| 14-17 | Local PDF, crew/download PDF, admin PDF and record created | the script checks the files |
| 18 | Admin unreachable: record queued, sent later | config `admin-unreachable.json` + outbox count |
| 19 | Scheduled vs reported sees the report | Project setup |
| 20 | The normal upload still works | the script makes a test download folder |
| S1 | The Schedule itself still works as before | open it as usual |
| RO | Schedule files unchanged | SHA-256 before and after |

## Comparing with the board

`schedule-check.txt` lists, for each date, every entry Crew Upload sees: who, type, project, number,
comments, task, the date range, and whether it is PENDING or OVERRIDDEN. It also lists the crews Crew
Upload builds and anyone with more than one entry. Open the Schedule on the same week and compare.
Try to cover:

- **A normal assignment.** Same person, project, type and dates.
- **An overridden assignment** (marked OVERRIDDEN): shows as the board shows it, after the other PM's change.
- **A multi-day assignment.** Listed on every day of its range, weekends included (the board draws
  weekend days when weekends are shown).
- **Time off.** OFF entries match. Generated 9-80 Fridays and 4-10s are not listed: Crew Upload does not
  use them, and they never create work.
- **More than one entry in a day**, if there is one (marked `<-- more than one entry this day`). Each
  one is offered as a separate choice on the report.
- **Crews.** Field entries on the same project on the same day are one crew. An office day on the
  same project is not part of the field crew. `withIds differs` notes show where the app's own links
  disagree.

Pending requests are listed but marked PENDING. The board shows them as pending; Crew Upload never uses
them.

If Node.js is on the PC, the exact check can be run too (see `tests\schedule-compat\README.md`). It runs
the Schedule's own `assembleState()` on the same folder and diffs it with `crewupload-assembled.json`.
Without Node, copy the schedule folder (a copy is just as good, and read-only) and the result can be
run elsewhere.
