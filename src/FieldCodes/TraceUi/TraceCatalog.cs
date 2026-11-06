using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldCodes.TraceUi
{
    /// <summary>How big a ribbon button is. The palette shows every button the same size.</summary>
    public enum TraceButtonSize
    {
        Large = 0,
        Small = 1
    }

    /// <summary>
    /// One TRACE button. <see cref="Command"/> is the existing command it runs; users only
    /// ever see <see cref="Text"/> and <see cref="Tip"/>.
    /// </summary>
    public sealed class TraceButton
    {
        public string Text { get; set; }
        public string Tip { get; set; }
        public string Command { get; set; }
        public string Icon { get; set; }
        public TraceButtonSize Size { get; set; }
    }

    /// <summary>A named group of buttons: one ribbon panel, one palette section.</summary>
    public sealed class TraceGroup
    {
        public string Title { get; set; }
        public IList<TraceButton> Buttons { get; set; }
    }

    /// <summary>
    /// The single list of TRACE tools. The ribbon and the palette are both built from it, so
    /// they cannot disagree, and a test checks every entry against the commands that really
    /// exist. Nothing here is a placeholder.
    ///
    /// Production, sheet, easement, exhibit and DIP tools are deliberately absent: they are
    /// not part of TRACE 1.0.
    /// </summary>
    public static class TraceCatalog
    {
        /// <summary>Opens the palette.</summary>
        public const string OpenCommand = "TRACE";

        /// <summary>Runs the steps the technician ticked in the palette.</summary>
        public const string RunCommand = "TRACERUN";

        private static TraceButton B(string text, string tip, string command, string icon, TraceButtonSize size)
        {
            return new TraceButton { Text = text, Tip = tip, Command = command, Icon = icon, Size = size };
        }

        public static IList<TraceGroup> Groups
        {
            get
            {
                const TraceButtonSize L = TraceButtonSize.Large;
                const TraceButtonSize S = TraceButtonSize.Small;
                return new List<TraceGroup>
                {
                    new TraceGroup { Title = "Cleanup", Buttons = new List<TraceButton>
                    {
                        B("Clean up", "Remove everything TRACE made. Survey geometry and your own drafting are not touched.", "TRACECLEAN", "clean", L),
                        B("Draw order", "Put symbols, linework and labels in the right display order.", "TRACEORDER", "order", S)
                    }},
                    new TraceGroup { Title = "Labels", Buttons = new List<TraceButton>
                    {
                        B("Point labels", "Label points, avoiding existing work. Labels you moved by hand stay where you put them.", "TRACELABELS", "labels", L),
                        B("Spot elevations", "Place spot elevation markers and text.", "TRACESPOT", "spot", S),
                        B("Tags", "Number features and keep the same numbers on every run.", "TRACETAGS", "tags", S)
                    }},
                    new TraceGroup { Title = "Trees", Buttons = new List<TraceButton>
                    {
                        B("Trees", "Read tree points and finish them: sizes, species and labels.", "TRACETREES", "trees", L),
                        B("Driplines", "Draw tree driplines, trimmed to their outer outline.", "TRACEDRIP", "drip", S)
                    }},
                    new TraceGroup { Title = "Linework", Buttons = new List<TraceButton>
                    {
                        B("Label line", "Click a line, slide the preview, click to place its label.", "TRACELABELLINE", "labelline", L),
                        B("Label between", "Label the surface between two edges.", "TRACELABELBETWEEN", "between", S),
                        B("Stairs", "Select the stair lines; the step count labels itself.", "TRACELABELSTAIRS", "stairs", S),
                        B("All lines", "Label every recognised line in one pass (off unless enabled in Setup).", "TRACELINELABELS", "labelline", S),
                        B("Line inventory", "List the linework TRACE recognises and what it could not identify.", "TRACELINES", "review", S)
                    }},
                    new TraceGroup { Title = "Drafting", Buttons = new List<TraceButton>
                    {
                        B("Draw line", "Draft a line by bearing and distance, with its annotation.", "TRACEDRAWLINE", "drawline", L),
                        B("Clean drafted", "Remove drafted-line annotation, or the drafted lines after you confirm.", "TRACEDRAFTCLEAN", "clean", S)
                    }},
                    new TraceGroup { Title = "Tables", Buttons = new List<TraceButton>
                    {
                        B("Schedule", "Build the schedule table.", "TRACETABLE", "table", L),
                        B("Control table", "List survey control and found monuments.", "TRACECONTROL", "control", S),
                        B("Legend", "Build the legend of line features in the drawing.", "TRACELEGEND", "legend", S)
                    }},
                    new TraceGroup { Title = "Setup", Buttons = new List<TraceButton>
                    {
                        B("Profiles & Standards", "Layers, labels, codes and line features. Choose or edit your standards.", "TRACESETUP", "setup", L),
                        B("Switch profile", "Show, choose, save or clear this drawing's profile.", "TRACEPROFILE", "profiles", S),
                        B("Diagnostics", "Show which configuration files TRACE is using.", "TRACEWHERE", "diag", S)
                    }}
                };
            }
        }

        /// <summary>Every button, flattened.</summary>
        public static IList<TraceButton> AllButtons
        {
            get { return Groups.SelectMany(g => g.Buttons).ToList(); }
        }

        /// <summary>
        /// Names for the pipeline steps in the palette's Run TRACE list, keyed by the
        /// pipeline step name. The pipeline decides the order; this only supplies wording.
        /// </summary>
        public static IDictionary<string, string> StepTitles
        {
            get
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "FTFPOINTS", "Points and trees" },
                    { "FTFDRIP", "Driplines" },
                    { "FTFLINELABELS", "All line labels" },
                    { "FTFLABELS", "Point labels" },
                    { "FTFTAGS", "Tags" },
                    { "FTFTABLE", "Schedule" },
                    { "FTFORDER", "Draw order" }
                };
            }
        }

        /// <summary>Wording for a step, falling back to the name it was given.</summary>
        public static string StepTitle(string stepName)
        {
            string title;
            return StepTitles.TryGetValue(stepName ?? string.Empty, out title) ? title : stepName;
        }
    }
}
