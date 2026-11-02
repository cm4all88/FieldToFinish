# Settings levels: one configuration, five layers

FTF's behaviour settings (`ftf-settings.json`: text heights, layers, labels, standards and so on)
form a single configuration. Each level holds only the values it changes. The levels are merged
one value at a time, and the highest level that sets a value wins:

| Priority    | Level           | File                                                              | Who it is for |
|-------------|-----------------|-------------------------------------------------------------------|---------------|
| 5 (highest) | **Project**     | `ftf-settings.json` beside the drawing                            | this survey project |
| 4           | **Profile**     | `%APPDATA%\FieldToFinish\profiles\<name>.json` (the profile the drawing selects) | a client or project standard |
| 3           | **My defaults** | `%APPDATA%\FieldToFinish\ftf-settings.json`                       | one person, every drawing |
| 2           | **Office**      | `ftf-settings.json` in the plugin folder, beside `FieldCodes.Cad.dll` | the whole office |
| 1 (lowest)  | **Built-in**    | compiled into FTF                                                  | everything not set anywhere else |

**My defaults sit below Profile and Project.** A person's own defaults apply wherever the
project and its profile say nothing, and they can never override a value the project or its
profile sets.

Interface preferences that are personal and not drafting standards, such as where the FTF window
was, its size, and the last Process preset, stay in `%APPDATA%\FieldToFinish\ui-state.json`. They
are never part of any settings level.

The rules file (`rules.json`, the field-code grammar) has its own levels (drawing > office >
factory, see `RulesStore`) and is not covered here.

## How values merge

* Sections and other JSON objects merge key by key. A project file that holds only
  `{ "labels": { "ringCount": 6 } }` changes that one value. Every other label setting still
  comes from the levels below.
* Lists, such as protected layers and line types, and single values **replace**. A list is never
  merged item by item.
* The merged result is read into the settings model the same way a single file used to be, so
  every command sees one complete settings object.

## Where each value comes from

**Settings > Where Values Come From** (`ProvenancePage`) lists every setting with:

* **Setting**, as a readable name such as *Point Labels > Text height (plotted)*
* **Value in use**
* **From**: Built-in, Office, My defaults, Profile "name", or Project
* **Overridden values**: what the other levels say, for example `My defaults: 6; Built-in: 4`

The table can be searched and sorted. It can be filtered to one level or to the values that
override another level. **Inherit** removes the selected value from its level, so the level below
supplies it. **Clear Level...** removes every value from the level chosen under *From*. Both keep a
`.bak` copy of the file. Both need any unsaved edits in the window to be saved or discarded first.

The page shows each level's file, how many values it sets, and its state: complete, hidden,
or protected by a PIN.

## Saving

The footer of the Settings window chooses the level:

* **Project (this drawing's folder)**: the default when the drawing has been saved.
* **Project profile "name"**: shown when the drawing selects a profile.
* **My defaults (all drawings, below project)**: the default for a drawing that has not been saved.
* **Office standard (authorization required)**

The line under the buttons names the file the next save goes to.

What a save writes:

* Only what you **changed** in the window goes to the chosen level. Values that came from other
  levels are never copied. For example, saving to the office standard from a drawing that has a
  project file does not copy the project's overrides into the office standard.
* Project, profile and My-defaults files keep only the values that **differ from what the level
  inherits**. A value that has become equal to the inherited one is removed from the file. Later
  changes to the office standard therefore still reach every project that does not override them.
* There is one exception, so that a project does not depend on who saved it. A project or profile
  value that equals the inherited value only because of the saver's own defaults is kept. A
  colleague with different defaults still gets the project's value.
* The **office** file is written whole: every setting, so it reads as one complete standard and
  does not change when a new build changes a built-in default.
* A change saved below a level that overrides it, such as a change saved to My defaults while
  the project file sets the same value, is saved. FTF then reports that the drawing keeps the
  higher level's value.
* **Save As Profile** (Profiles page, or `FTFPROFILE` > SaveAs) stores the settings in use as the
  values that differ from the office standard and your defaults.
* **Export** writes one complete file. **Import** of a complete file replaces every value in the
  window. Import of a file of overrides changes only the values it holds.

## Files from earlier versions

Files written before levels existed have `"version": "1"` and hold every setting. They keep their
old meaning: such a file is **complete**, and the levels below it are not used. For example, an
old full `ftf-settings.json` beside a drawing still decides everything for that drawing.

The first save to that level rewrites the file as an override file with `"version": "2"`. The
rewritten file keeps only the values that really differ from the levels below it, and from then
on office changes reach that level.

Inherit on a complete file first reduces the file in the same way, so the level below shows
through.

Hand-written test files such as `deploy/smoke-test.ps1`'s `{ "version": "1" }` and
`tests/LiveSmoke/*/ftf-settings.json` therefore still isolate their drawings from the user's and
the office's settings.

A file that cannot be read is reported with its path and level, and the command stops. FTF never
skips such a file silently. A file can fail to be read because it is invalid JSON, empty, not a
JSON object, or holds a value of the wrong type.

When no settings file exists anywhere, the legacy keys in `rules.json` seed the built-in values,
as before. The first save writes them to a file.

## Office authorization

Changing the office standard requires two things:

1. **A writable office location.** FTF checks that it can create a file in the plugin folder, and
   that the office file is neither read-only nor locked.
2. **The office PIN, when one is set.** The office file can carry:

   ```json
   "admin": { "pinHash": "<sha-256 hex>", "salt": "<random hex>" }
   ```

   `pinHash` is SHA-256 of the UTF-8 text `salt + PIN`. The PIN itself is never stored.

When no PIN is set, the first office save offers to set one. Setting a PIN is optional.

Inherit and Clear Level on the office level also ask for the PIN. Project, profile and My-defaults
saves never ask for it.

The PIN guards against an accidental change at the wrong level. It is not protection against
someone who can edit files in the plugin folder. That person can remove the `admin` section, which
is also how to reset a forgotten PIN.

**Deployment caveat:** an FTF update (`UPDATEFTF` / `install.ps1`) replaces the whole plugin
folder, including an office `ftf-settings.json` saved there. Until the release ships the office
file, keep a copy. To ship it, copy it into `Contents\2024` from the repository, or have the
installer carry it over.

## For developers

* `FieldCodes.Settings.SettingsLayers` has these members:
  * `Resolve` and `Refresh`
  * `SaveChanges`, `SaveProfile`, `Inherit` and `ClearLevel`
  * `ReadForImport`
  * `Merge`, `Diff`, `Leaves`, `Lookup` and `Humanize`
  * `FileStamp`, which the session cache uses to notice a changed file
* `SettingsResolution` has these members:
  * `Levels`: each level's path, whether it exists, whether it is complete or hidden, and its
    value count
  * `Provenance`: for each dotted JSON path, the value, the level it comes from, the overridden
    values, and where the value would come from if inherited
  * `PathFor(level)`
  * The existing `Source`, `LoadedFrom`, `ProfileName`, `MissingProfile` and `MigratedFromRules`
    keep their meaning. `Source` and `LoadedFrom` name the highest level that has a file.
* `FtfSettings.Save(path)` on settings that came from `Resolve` writes that level's differences
  when `path` is one of the resolution's level files. Any other path gets a complete copy.
* `OfficeAuthorization` handles PIN hashing and verification and the writable-folder check.
* Tests are in `tests/FieldCodes.Tests/SettingsLayersTests.cs`.
