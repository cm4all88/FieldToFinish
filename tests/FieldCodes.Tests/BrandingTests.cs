using System.Reflection;
using System.Reflection.Emit;
using FieldCodes;
using FieldCodes.Editing;

namespace FieldCodes.Tests;

public sealed class BrandingTests
{
    private static Assembly Stamped(string key, string value)
    {
        var asm = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Stamped" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        var ctor = typeof(AssemblyMetadataAttribute).GetConstructor(new[] { typeof(string), typeof(string) })!;
        asm.SetCustomAttribute(new CustomAttributeBuilder(ctor, new object[] { key, value }));
        return asm;
    }

    [Theory]
    [InlineData("Default", "TRACE")]
    [InlineData("default", "TRACE")]
    [InlineData("Parametrix", "FieldToFinish")]
    [InlineData("", "FieldToFinish")]
    [InlineData(null, "FieldToFinish")]
    [InlineData("Anything else", "FieldToFinish")]
    public void EditionMapsToFolder(string? edition, string folder)
        => Assert.Equal(folder, Branding.FolderForEdition(edition));

    [Fact]
    public void NoStampKeepsTheLegacyFolder()
        => Assert.Equal("FieldToFinish", Branding.Resolve(new[] { typeof(Branding).Assembly }, "TestKeyNobodySets"));

    [Fact]
    public void CommercialStampSelectsTrace()
    {
        const string key = "TestEditionKeyA";
        Assert.Equal("TRACE", Branding.Resolve(new[] { Stamped(key, "Default") }, key));
    }

    [Fact]
    public void InternalStampKeepsFieldToFinish()
    {
        const string key = "TestEditionKeyB";
        Assert.Equal("FieldToFinish", Branding.Resolve(new[] { Stamped(key, "Parametrix") }, key));
    }

    [Fact]
    public void UnitTestProcessKeepsTheLegacyFolderAndBothStoresAgree()
    {
        // Nothing in the test process carries the real stamp, so existing behaviour holds.
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.Equal(Path.Combine(appData, "FieldToFinish"), RulesStore.OfficeDirectory());
    }
}
