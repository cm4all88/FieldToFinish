# Build TRACE and test it in Civil 3D 2024 (Windows)

You need: Windows 10/11, Civil 3D 2024 installed in the default folder, internet access.
Untested instructions: if any step fails, copy the red error text and send it to me.

## 1. One-time setup
1. Install **Git for Windows** (git-scm.com) and the **.NET 8 SDK** (dotnet.microsoft.com/download). Accept the defaults.
2. Open **PowerShell** and run:
```powershell
mkdir C:\dev -Force
cd C:\dev
git clone https://github.com/cm4all88/FieldToFinish
cd FieldToFinish
git checkout claude/sleepy-allen-e5i3ru
```
(The folder must be `C:\dev\FieldToFinish`: the existing automated test scripts expect it.)

## 2. Build and run the non-CAD tests
```powershell
dotnet build src\FieldCodes.Cad\FieldCodes.Cad.csproj
dotnet test tests\FieldCodes.Tests
```
- The first command builds TRACE (neutral configuration). It must end with `Build succeeded`. If it says Civil 3D was not found, add `-p:C3D2024="D:\path\to\AutoCAD 2024"`.
- The second should end with `Passed! ... 968`. Send me the last two lines either way.

## 3. Install into Civil 3D
Close Civil 3D completely, then:
```powershell
powershell -ExecutionPolicy Bypass -File deploy\install-dev.ps1
```
It must print `Installed Civil 3D 2024` and `Factory rules edition: Default`. (To remove it later: add `-Uninstall`.)

## 4. First look in Civil 3D
1. Start Civil 3D 2024 and open any survey drawing (a copy, not a live project).
2. Look for an **FTF** tab on the ribbon. Type `FTFWHERE` and press Enter.
3. Check the text shows the rules file from the TRACE install folder and an office folder under `%APPDATA%\TRACE`.
4. Type `FTFSETUP`. The settings window should open. Close it.
Take a screenshot of the ribbon and of the `FTFWHERE` output.

## 5. The five workflows (use a drawing copy; run each twice)
For each: run the command, look at the result, run it again, and check nothing doubled up.
| # | Do this | Pass if |
|---|---|---|
| 1 | `FTFCLEAN` after running any other command | It removes only TRACE-made items; a second run removes nothing more |
| 2 | `FTFLABELS`, move one label by hand, run `FTFLABELS` again | Your moved label stays where you put it; the count of labels does not grow |
| 3 | On a drawing with tree points coded like `CON 18 . 25`: `FTFTREES`, then `FTFDRIP`, then `FTFLABELS` | Drip circles appear around trees, trimmed where they overlap; running again gives no duplicates |
| 4 | `FTFLINES` then `FTFLABELLINE` and click a line | With the neutral default, lines are listed as unknown. That is expected until line features are added (see step 7) |
| 5 | `FTFDRAWLINE` (draw a boundary line by bearing and distance), then `FTFDRAFTCLEAN` | The line gets a bearing/distance label; cleanup behaves as the prompt says |
Note anything that errors, looks wrong, or feels slow. Photos of the screen are fine.

## 6. Run the existing automated smoke test (optional, 5 minutes)
```powershell
powershell -ExecutionPolicy Bypass -File tests\LiveSmoke\run-livesmoke.ps1
```
Send me `tests\LiveSmoke\draft_out.log`.

## 7. Check your internal Parametrix setup is untouched
Only if you want to confirm the internal version still works:
```powershell
dotnet build src\FieldCodes.Cad\FieldCodes.Cad.csproj -p:TraceEdition=Parametrix
powershell -ExecutionPolicy Bypass -File deploy\install-dev.ps1 -Edition Parametrix
```
Use the same word in both commands. This uses `%APPDATA%\FieldToFinish` and your Parametrix rules, exactly as before. To go back to TRACE, repeat steps 2 and 3 without the extra words.

## What to send me
The last lines from steps 2 and 3, the `FTFWHERE` screenshot, and for each workflow in step 5: worked / did not, plus any error text.
