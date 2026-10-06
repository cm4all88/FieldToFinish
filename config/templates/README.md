# Crew Upload templates

Drop the office's blank forms here with these names (see `documents` in `../job-folders.json`):

| File | Button |
|---|---|
| `Field Notes.docx` | New field notes (saved as `...-TOPO-FN.docx`) |
| `As-Built Notes.docx` | New as-built notes (saved as `...-TOPO-ASB.docx`) |

They are copied beside `CrewUpload.exe` on build. Any file type works -- the new document keeps the
template's extension. When a template is missing the button still works and starts a plain text sheet
headed with the project, date, crew and work type, and the status bar says which template was not found.
