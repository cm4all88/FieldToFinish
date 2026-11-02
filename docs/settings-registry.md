# Settings registry

Every FTF command that has options gets them on a Settings page, and every Settings page
registers itself: its workflow group, its name in the navigation, its place in the group, the
settings sections it edits and the commands that read them. The Settings window builds its
navigation from those registrations, Settings > Commands links commands to pages from them, and
the tests check them. There is no list of pages to edit anywhere else.

| Piece | Where |
|---|---|
| The attribute, the registry, group order, the workflow follow-up allow-list | `src/FieldCodes/Settings/SettingsRegistry.cs` |
| Reading the plugin's registrations at run time | `src/FieldCodes.Cad/Setup/SettingsPages.cs` |
| Registrations for pages whose file is not yours | `src/FieldCodes.Cad/Setup/SettingsPageRegistrations.cs` |
| The navigation built from them | `src/FieldCodes.Cad/Ui/SettingsPanel.cs` (constructor) |
| Saved workflows | `src/FieldCodes/Settings/WorkflowSettings.cs`, `ProcessPresets.Build(WorkflowDefinition, ...)` |
| Settings > Commands, My Commands | `src/FieldCodes.Cad/Setup/CommandsPage.cs`, `src/FieldCodes/Commands/CommandCatalog.cs`, `UserRibbonPanel.cs` |
| Tests | `tests/FieldCodes.Tests/SettingsRegistryTests.cs`, `WorkflowTests.cs`, `CommandCatalogTests.cs` |

## Registering a page

Put the attribute on the line directly above the page's class (after its doc comment):

```csharp
[SettingsPage(Group = "FieldToFinish", Label = "Tags", Order = 90, Sections = "tags", Commands = "FTFTAGS,FTFLABELALL")]
internal sealed class TagsPage : SetupPage
```

That is all. The page appears in the Settings window under its group at its order, Settings >
Commands lists it against each command it names, and `Context.Navigate("Tags")` reaches it.

| Argument | Meaning |
|---|---|
| `Group` | `FieldToFinish`, `Survey Drafting`, `Boundary and Easements` or `Administration` (any casing). Any other name makes a new group, shown after those four, alphabetically. |
| `Label` | The name in the navigation, and the key `Context.Navigate` / `SettingsPanel.SelectSection` use. Unique across all pages. |
| `Order` | Place in the group, smallest first; ties are broken by label. Today's pages are spaced by 10, so a new page fits between two without renumbering. |
| `Sections` | The settings sections the page edits, comma separated, by their key in `ftf-settings.json` (`"tags"`, `"annotation"`, `"lineLabels"`). Empty for a page that edits none (it reads rules.json, or edits per-user state). |
| `Commands` | The commands whose behaviour the page changes, comma separated, exactly as typed. `*` means every command (General, Layers, Profiles); it can be combined with names (`"FTFPROFILE,*"`). Every command the page's own `AffectedCommands` caption names must be here (tested). |
| `Aliases` | Labels the page had before, comma separated, so links using the old name still land. (The aliases from before the registry stay in `SettingsPanel.Renamed`.) |
| `Unlisted` | A reason. The page is registered but not shown (FieldCodesPage today). |
| `Page` | Only on an assembly registration (below). |

Write every argument as a literal -- a string, a number, `typeof(...)` -- with no constants or
expressions: the tests read the registrations from the source text, because the Civil 3D project
cannot be loaded without Civil 3D. A registration they cannot read fails the tests, with the
reason.

### A page whose file is not yours

When the page's file belongs to someone else's change (LayersPage and PointFeaturesPage today),
register it from the assembly instead, in `src/FieldCodes.Cad/Setup/SettingsPageRegistrations.cs`:

```csharp
[assembly: SettingsPage(Page = typeof(FieldCodes.Cad.Setup.LayersPage), Group = "FieldToFinish", Label = "Layers", Order = 20, Sections = "general", Commands = "*")]
```

The result is identical. A page must be registered exactly once, one way or the other.

### Pages arriving from parallel work

A page added on another branch (a "Where values come from" provenance page, text and leader
style pages, symbol pages) registers the same way once merged: one `[SettingsPage(...)]` line above
its class. Until it does, `SettingsRegistryTests.EverySetupPageInThePluginIsRegistered` fails and
names it, and at run time the window leaves it out and writes "has no [SettingsPage] registration"
to the FTF log. Pick the group by the kind of work the page configures (a style page used by
labels: `FieldToFinish`; a page about where settings come from: `Administration`) and an `Order`
between the neighbours it belongs beside. No test needs updating: the navigation test checks
today's pages keep their groups and order, and allows pages between them.

## Adding a command's settings

1. The settings: a class implementing `ISettingsSection` plus one property on `FtfSettings` (with
   its `[JsonProperty("key")]`, constructor default, `FillMissingSections` line and entry in
   `Sections`), as before. Update the section count in `FtfSettingsTests`.
2. The page: a `SetupPage` with `[SettingsPage(...)]`, naming the new section in `Sections` and the
   command in `Commands`.
3. Nothing else. The navigation, Settings > Commands (with its link to the page) and My Commands
   pick up both the command (from its `[CommandMethod]`) and the page.

If a section deliberately has no page, add its key to `SettingsRegistry.SectionsWithoutPage` with
the reason; otherwise `EverySettingsSectionIsEditedByARegisteredPage` fails.

## Settings > Commands

Every command the plugin registers, read at run time from its own `[CommandMethod]` attributes
(commands flagged `NoHistory` -- the Dip Builder's and the comparison's queued actions -- are
internal and left out), so a new command appears without being listed. For each: what it does
(its ribbon tooltip, or the processing stage's description), the names it also answers to (FTFL),
where it is on the ribbon, the settings pages that change it (links to each), and whether it is a
processing stage or can follow a workflow. Searchable; every column sorts.

### My Commands

Ticking a command there puts it on a **My Commands** panel at the end of the FieldToFinish ribbon
tab. The list is the person's own, saved at once in `%APPDATA%\FieldToFinish\ui-state.json` as
`"quickCommands": ["FTFLABELS", "FTFLINES"]` -- never in a settings file or profile, so the office
ribbon is the same for everyone. The panel is added when the ribbon is built (Civil 3D start-up),
copying each command's button (name, icon, tooltip) when it is on the ribbon, a text button
otherwise. **Rebuild Ribbon Now** rebuilds the FieldToFinish tab in place the way a reload does
(replace by tab id, put back at the same position). `RibbonLayout` is unchanged and its tests
still describe the office ribbon; the panel is appended at run time only.

## Workflows

Settings > Workflows (FieldToFinish group) holds named processing runs beside the built-in
presets. In `ftf-settings.json`:

```json
"workflows": {
  "items": [
    {
      "name": "Trees only (no tags)",
      "description": "Driplines and tree labels.",
      "stages": [ "FTFDRIP", "FTFLABELS", "FTFORDER" ],
      "families": [ "Trees" ],
      "thenRun": [ "FTFLINES" ]
    }
  ]
}
```

- `stages`: from `ProcessStage.InOrder` (FTFPOINTS, FTFORIENT, FTFDRIP, FTFLINELABELS, FTFLABELS,
  FTFTAGS, FTFTABLE, FTFORDER). The page normalises them on save with `ProcessPresets.Normalize`:
  each once, in pipeline order, draw order added when anything it bands runs.
- `families`: `AnnotationCategory` names. Empty means the families set on Label All.
- `thenRun`: commands queued on the command line once the stages finish, from
  `SettingsRegistry.WorkflowFollowUps` only: FTFLINES, FTFKEYMAP, FTFEASEMENTEXPORT -- commands
  that never stop to ask anything. FTFLEGEND and FTFCONTROL are not allowed: each asks for a
  corner when there is no legend or table yet (a test checks the allowed ones do not prompt).

A file written before workflows existed simply has none. Validation refuses a workflow without a
name or stages, two with the same name, a name that is a built-in preset's, a name whose FTFRUN
keyword is a built-in choice or another workflow's, an unknown stage and a follow-up that is not
allowed.

A workflow runs exactly like a preset: `ProcessPresets.Build(workflow, annotationSettings)` is the
Custom plan for its stages and families (same dependency rules and adjustments) under the
workflow's name, run with `AnnotationRun.Use`. Its follow-ups are queued when that scope closes
(`AnnotationRun.FollowUp`), so FTFRUN and the window behave the same.

- **Process page**: saved workflows are listed after the built-in presets and before Custom. The
  list refreshes when opened, so a workflow saved in Settings shows without reopening the window;
  the last choice is remembered (`"processPreset": "Workflow:<name>"`). A Workflows... link opens
  the Settings page.
- **FTFRUN**: `Process [Standard/Trees/Annotations/All/<keywords>] <Standard>:`. A workflow's
  keyword is its name's letters and digits, each word capitalised ("Trees only (no tags)" is
  `TreesOnlyNoTags`; a name starting with a digit gets `Run` in front; at most 31 characters).

New CAD functionality still needs code -- a new stage is a new command and a new `FtfPipeline`
step. Putting existing stages, families and reports together in a new way does not.
