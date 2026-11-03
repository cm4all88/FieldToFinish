# Real-drawing cases

The cases that run on real project drawings are **not in this repository**. The repository is public, and the
cases name real projects, parcels and clients. They live beside the drawings, in `C:\dev\FTF-RealWorld`
(or `$env:FTF_REALWORLD`):

```
C:\dev\FTF-RealWorld\
  refs\               the original drawings (copied for every run, never opened for write)
  cases\cases.psd1    @{ '<case>' = 'refs\<folder>\<drawing>.dwg'; ... }
  cases\<case>.txt    the command lines for that case
  runs\<case>\        each run's copy, log, report and PDFs
```

- `..\export-real-cases.ps1` restores the original nine cases from git history (commit `e76f6c2`, the last one
  that held them). Nothing was deleted from history; see the commit that removed them.
- `example-synthetic.txt` shows the format with invented coordinates, names and numbers. Use invented values for
  anything committed here.
