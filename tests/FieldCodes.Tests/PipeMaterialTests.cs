using System.Linq;
using FieldCodes.Settings;
using FieldCodes.Utilities;
using Xunit;

namespace FieldCodes.Tests
{
    /// <summary>
    /// How a material is written down, from whichever direction it arrives. Two spellings of
    /// one material means two rows in a schedule that should have one.
    /// </summary>
    public class PipeMaterialTests
    {
        [Theory]
        [InlineData("DI", "DIP")]
        [InlineData("di", "DIP")]
        [InlineData(" D.I. ", "DIP")]
        [InlineData("D.I", "DIP")]
        [InlineData("DIP", "DIP")]
        public void ductile_iron_is_written_one_way(string written, string expected)
        {
            Assert.Equal(expected, PipeMaterials.Normalize(written));
        }

        [Theory]
        [InlineData("rcp", "RCP")]
        [InlineData(" cpp ", "CPP")]
        [InlineData("ribbed pvc", "RIBBED PVC")]
        public void anything_else_is_kept_as_written_in_capitals(string written, string expected)
        {
            Assert.Equal(expected, PipeMaterials.Normalize(written));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void nothing_given_is_nothing_recorded(string written)
        {
            Assert.Null(PipeMaterials.Normalize(written));
        }

        [Fact]
        public void cpp_is_one_of_the_materials_offered()
        {
            Assert.Contains("CPP", new UtilitySettings().Materials);
        }

        [Fact]
        public void the_list_offers_one_spelling_of_ductile_iron()
        {
            var materials = new UtilitySettings().Materials;
            Assert.Contains("DIP", materials);
            Assert.DoesNotContain("DI", materials);
        }

        [Fact]
        public void a_note_written_DI_is_still_read_as_a_material()
        {
            // The crew writes DI; the office writes DIP. Dropping DI from the list must not
            // make a note saying DI unreadable.
            var settings = new UtilitySettings();
            var parsed = new DipNoteParser(settings).Parse("PT 1045 SDMH\n12 DI N 4.10");

            var pipe = parsed.Structures.Single().Pipes.Single();
            Assert.Equal("DIP", pipe.Material);
            Assert.Equal(12, pipe.WidthIn);
        }

        [Fact]
        public void a_note_written_DIP_reads_the_same_way()
        {
            var settings = new UtilitySettings();
            var parsed = new DipNoteParser(settings).Parse("PT 1045 SDMH\n12 DIP N 4.10");

            Assert.Equal("DIP", parsed.Structures.Single().Pipes.Single().Material);
        }

        [Fact]
        public void a_pipe_entered_as_DI_in_the_panel_is_recorded_as_DIP()
        {
            var pipe = new QuickPipeEntry
            {
                Direction = DirectionShortcuts.For("N"), SizeIn = 8, Material = "DI",
                MeasuredDip = 4.1, Reference = MeasurementReference.Invert
            }.Create();

            Assert.Equal("DIP", pipe.Material);
        }
    }
}
