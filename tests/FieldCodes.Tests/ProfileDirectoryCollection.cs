namespace FieldCodes.Tests;

/// <summary>
/// FtfSettings.ProfileDirectoryOverride is process-wide. Every test that sets it runs in
/// this collection, alone and one at a time; otherwise two classes race, one resets the
/// override to null while the other resolves, and the other resolves from the real
/// %APPDATA% folder instead of its temp folder (seen as an intermittent failure in
/// DraftingProfileTests.AProjectFileBesideTheDrawingStillWins).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProfileDirectoryCollection
{
    public const string Name = "ProfileDirectoryOverride";
}
