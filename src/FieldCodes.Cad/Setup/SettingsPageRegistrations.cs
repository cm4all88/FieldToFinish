using FieldCodes.Settings;

// Settings pages registered from the assembly rather than on their own class. Use this only for a
// page whose file is not yours to change; everywhere else, put [SettingsPage(...)] directly above
// the page's class (docs/settings-registry.md). The registration is the same either way.

[assembly: SettingsPage(Page = typeof(FieldCodes.Cad.Setup.LayersPage), Group = "FieldToFinish", Label = "Layers", Order = 20, Sections = "general,layerStandards", Commands = "FTFLAYERUPDATE,FTFCHECKSTANDARDS,*")]
[assembly: SettingsPage(Page = typeof(FieldCodes.Cad.Setup.PointFeaturesPage), Group = "FieldToFinish", Label = "Point Features", Order = 30, Sections = "styles", Commands = "FTFPOINTS,FTFLABELS,FTFTAGS,FTFLABELALL,FTFRESTYLE")]
