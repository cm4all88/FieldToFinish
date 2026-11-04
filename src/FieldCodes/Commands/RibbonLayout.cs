using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FieldCodes.Commands
{
    /// <summary>How big a ribbon button is drawn.</summary>
    public enum RibbonButtonSize
    {
        /// <summary>A 16 px icon with its label beside it, stacked three to a column.</summary>
        Small,

        /// <summary>A 32 px icon with its label underneath: the panel's main commands.</summary>
        Large
    }

    /// <summary>One button: what it says, the command it types, its icon and its tooltip.</summary>
    public sealed class RibbonButtonLayout
    {
        internal RibbonButtonLayout(string label, string command, string icon, RibbonButtonSize size, bool major, string tip)
        {
            Label = label;
            Command = command;
            Icon = icon;
            Size = size;
            IsMajor = major;
            Tip = tip;
        }

        /// <summary>The text on the button. A large button may break it over two lines with \n.</summary>
        public string Label { get; private set; }

        /// <summary>The command the button types, exactly as it is registered. Never renamed here.</summary>
        public string Command { get; private set; }

        /// <summary>The icon: the name of the method in FtfIcons that draws it.</summary>
        public string Icon { get; private set; }

        /// <summary>Large or small.</summary>
        public RibbonButtonSize Size { get; private set; }

        /// <summary>
        /// A major command: a large button, or a small one that is a workflow of its own (Draw
        /// Order, Legend, Bench). The rest are steps or variants of their panel's main command
        /// (Rebuild Record, Restore Trim). A major command always has an icon nothing else uses.
        /// </summary>
        public bool IsMajor { get; private set; }

        /// <summary>What the button does, in one or two plain sentences. The ribbon shows it under
        /// the button's title, with the command name beneath.</summary>
        public string Tip { get; private set; }

        /// <summary>The label on one line, for the tooltip's title.</summary>
        public string Title { get { return Label.Replace('\n', ' '); } }

        /// <summary>For reading test failures.</summary>
        public override string ToString() { return Title + " (" + Command + ")"; }
    }

    /// <summary>A titled group of buttons on a tab.</summary>
    public sealed class RibbonPanelLayout
    {
        internal RibbonPanelLayout(string title, IList<RibbonButtonLayout> buttons)
        {
            Title = title;
            Buttons = new ReadOnlyCollection<RibbonButtonLayout>(buttons);
        }

        /// <summary>The panel's name, shown along its bottom edge.</summary>
        public string Title { get; private set; }

        /// <summary>The buttons, in the order they appear.</summary>
        public IReadOnlyList<RibbonButtonLayout> Buttons { get; private set; }
    }

    /// <summary>One ribbon tab: its id, its title and its panels.</summary>
    public sealed class RibbonTabLayout
    {
        internal RibbonTabLayout(string id, string title, IList<RibbonPanelLayout> panels)
        {
            Id = id;
            Title = title;
            Panels = new ReadOnlyCollection<RibbonPanelLayout>(panels);
        }

        /// <summary>The id the ribbon knows the tab by. Kept stable across builds, so a reload
        /// replaces the tab instead of adding a second one.</summary>
        public string Id { get; private set; }

        /// <summary>The name on the tab.</summary>
        public string Title { get; private set; }

        /// <summary>The panels, left to right.</summary>
        public IReadOnlyList<RibbonPanelLayout> Panels { get; private set; }

        /// <summary>Every button on the tab, panel by panel.</summary>
        public IEnumerable<RibbonButtonLayout> Buttons { get { return Panels.SelectMany(p => p.Buttons); } }
    }

    /// <summary>
    /// What is on FTF's ribbon, as plain data: four tabs, one per kind of work, so a drafter can
    /// tell at a glance where a tool lives.
    ///
    ///   FieldToFinish    the drawing being finished: process, points and symbols, labels,
    ///                    trees, utility features, settings and reports
    ///   Survey Drafting  the hand drafting on top of it: elevations, line labels, drafting
    ///                    utilities, sheets
    ///   Boundary         the record work: recorded surveys, calculations and closure, right of
    ///                    way and platted blocks, the base-map comparison
    ///   Easements        creating easements, their legal descriptions, exhibits and closure
    ///
    /// FtfRibbon (in the Civil 3D project) turns this into the real ribbon and draws each icon by
    /// name, so the layout can be tested here without Civil 3D: every command that was ever on
    /// the ribbon is still on it, every button types a command the plugin registers, and no two
    /// commands share an icon.
    ///
    /// A command may sit on more than one tab when both kinds of work use it (Draw Lines is
    /// drafting and boundary work), but never twice on the same tab. Command names are a public
    /// contract: a button can be relabelled freely, its command never.
    /// </summary>
    public static class RibbonLayout
    {
        /// <summary>The FieldToFinish tab. The id the single FTF tab always had.</summary>
        public const string FieldToFinishTabId = "FTF_RIBBON_TAB";

        /// <summary>The Survey Drafting tab.</summary>
        public const string DraftingTabId = "FTF_DRAFTING_RIBBON_TAB";

        /// <summary>The Boundary tab. The id the FTF Boundary tab already had.</summary>
        public const string BoundaryTabId = "FTF_BOUNDARY_RIBBON_TAB";

        /// <summary>The Easements tab.</summary>
        public const string EasementsTabId = "FTF_EASEMENTS_RIBBON_TAB";

        private static readonly ReadOnlyCollection<RibbonTabLayout> TabList =
            new ReadOnlyCollection<RibbonTabLayout>(new[] { FieldToFinish(), Drafting(), Boundary(), Easements() });

        /// <summary>The tabs, in the order they sit on the ribbon.</summary>
        public static IReadOnlyList<RibbonTabLayout> Tabs { get { return TabList; } }

        /// <summary>
        /// Tab ids an earlier FTF build used that this one no longer does. A session that loaded an
        /// older build first must not keep that tab beside the new ones, so FtfRibbon takes these off
        /// whenever it builds. Empty today: both ids the two-tab ribbon used are kept, by the
        /// FieldToFinish and Boundary tabs. An id dropped from <see cref="Tabs"/> belongs here.
        /// </summary>
        public static IReadOnlyList<string> RetiredTabIds { get { return Retired; } }

        private static readonly ReadOnlyCollection<string> Retired = new ReadOnlyCollection<string>(new string[0]);

        /// <summary>Every button on every tab.</summary>
        public static IEnumerable<RibbonButtonLayout> AllButtons { get { return Tabs.SelectMany(t => t.Buttons); } }

        /// <summary>Every command on the ribbon, each once.</summary>
        public static IEnumerable<string> Commands
        {
            get { return AllButtons.Select(b => b.Command).Distinct(StringComparer.OrdinalIgnoreCase); }
        }

        // ================================================================= the tabs

        private static RibbonTabLayout FieldToFinish()
        {
            return Tab(FieldToFinishTabId, "FieldToFinish",
                Panel("Process",
                    Large("Process\nDrawing", "FTFRUN", "Run",
                        "Runs every finishing stage in order: point symbols, driplines, labels, tree tags, the schedule and draw order. Safe to run again; each stage replaces only its own earlier output."),
                    Large("FTF\nWindow", "FTF", "Window",
                        "Opens the FieldToFinish window: status, review, settings and standards in one place. Nothing is processed until you press a button."),
                    Tool("Draw Order", "FTFORDER", "Order",
                        "Puts labels and symbols back above the linework and masks. Use it after pasting, exploding or a Civil 3D rebuild."),
                    Tool("Clean Up", "FTFCLEAN", "Clean",
                        "Removes everything the processing created. Survey points, linework and drafted lines are left untouched.")),

                Panel("Points & Symbols",
                    Large("Points", "FTFPOINTS", "Points",
                        "Reads every survey point's field code and places its symbol. Points that cannot be read go to the exception report."),
                    Large("Orient\nSymbols", "FTFORIENT", "Orient",
                        "Turns survey symbols to face the way the features do: signs, hydrants, light arms, valves and pavement arrows. Anything unsure is listed for review and left alone."),
                    Step("Reset Orient", "FTFORIENTRESET", "OrientReset",
                        "Puts every symbol Orient Symbols turned back the way it was, and removes its review graphics."),
                    Tool("Control Table", "FTFCONTROL", "Control",
                        "Draws or refreshes the table of control points and found monuments: point number, northing, easting, elevation and note."),
                    Tool("Legend", "FTFLEGEND", "Legend",
                        "Draws or refreshes a legend of the line types this drawing actually contains.")),

                Panel("Labels",
                    Large("Label\nEverything", "FTFLABELALL", "LabelAll",
                        "Labels every recognised line and every point, then fixes the draw order. Labels you placed or moved by hand are kept."),
                    Tool("Point Labels", "FTFLABELS", "Labels",
                        "Places point labels, masks and leaders around existing work. A label moved by hand stays where you put it."),
                    Tool("Update Styles", "FTFRESTYLE", "Restyle",
                        "Brings FTF's existing labels, tags and line labels up to the current text and leader styles, in place. Previews first; never moves a label or touches anything FTF did not draw."),
                    Tool("Gates", "FTFGATES", "Gates",
                        "Pairs the gate posts (FGP) along the fences and labels each gate between its posts with its width, such as 6' GATE. Unsure pairs go to the review list; no post is moved."),
                    Tool("Pair a Gate", "FTFGATE", "GatePair",
                        "Pick two gate posts to label the gate between them: confirms a pair FTF was unsure of, or changes one it made. Reruns keep it.")),

                Panel("Trees",
                    Large("Driplines", "FTFDRIP", "Drip",
                        "Draws tree driplines, trimmed to their outer edge where canopies overlap."),
                    Tool("Tree Tags", "FTFTAGS", "Tags",
                        "Numbers the trees with tags. A tree keeps the same number on every run."),
                    Tool("Tree Schedule", "FTFTABLE", "Schedule",
                        "Draws or refreshes the tree schedule table. A table you moved stays where it is.")),

                Panel("Utility Features",
                    Large("Dip\nBuilder", "FTFDIP", "Dips",
                        "Builds storm and sewer structures from field dips: pipes, connections, labels and QC, in one window."),
                    Step("Check Dips", "FTFDIPCHECK", "DipCheck",
                        "Finds structures whose surveyed point moved since their pipes and labels were drafted, and offers to rebuild each one."),
                    Tool("Field Notes PDF", "FTFDIPPDF", "DipNotesPdf",
                        "Saves the completed dips as printable field notes: measured depths in black, invert elevations in red, a sketch per structure. Never overwrites an issued file.")),

                Panel("Settings",
                    Large("Settings", "FTFSETUP", "Settings",
                        "Opens FTF's settings: layers, label formats, standards and every command's options."),
                    Tool("Profile", "FTFPROFILE", "Profile",
                        "Chooses the drafting profile this drawing uses, so everyone who opens it drafts to the same standard."),
                    Tool("Version", "FTFVERSION", "Version",
                        "Shows which FTF build is running, where it was loaded from, and whether it is the latest approved build."),
                    Tool("Office Update", "FTFUPDATE", "Update",
                        "Compares this machine's FTF with the office copy. A newer office copy installs itself when Civil 3D closes."),
                    Tool("Check Standards", "FTFCHECKSTANDARDS", "CheckStandards",
                        "Checks every layer, linetype, text style, multileader style and block the settings name against this drawing, and offers fixes. Nothing changes until you press a fix."),
                    Tool("Update Layers", "FTFLAYERUPDATE", "LayerUpdate",
                        "Lists every layer FTF uses whose colour, linetype, lineweight or plot setting differs from its standard, then asks before changing anything. Never touches entities.")),

                Panel("Reports",
                    Tool("Review List", "FTFREVIEW", "Review",
                        "Everything FTF could not decide, in one list: unknown codes, unpaired gate posts, unreadable fence heights, arrows needing review. Zoom to each, fix it, or mark it not applicable with a reason."),
                    Tool("Drafting QC", "FTFQC", "QcComplete",
                        "Marks the drawing Drafting QC Complete, refused while required review items are open. Never blocks saving or drafting."),
                    Tool("Linework", "FTFLINES", "Lines",
                        "A read-only inventory of the drawing's linework: what FTF recognises and what it does not. Nothing is changed."),
                    Tool("Blocks", "FTFBLOCKS", "Blocks",
                        "Lists the drawing's block definitions and checks them against the blocks the rules expect."),
                    Tool("Rules File", "FTFWHERE", "Where",
                        "Says where FTF looks for its rules file and what it found. Run this first when a command complains about the rules.")));
        }

        private static RibbonTabLayout Drafting()
        {
            return Tab(DraftingTabId, "Survey Drafting",
                Panel("Elevations",
                    Large("Spot\nElevations", "FTFSPOT", "Spot",
                        "Click to place spot elevations read off the TIN surface; each value moves clear of the ones already placed. Esc finishes."),
                    Large("Contours", "FTFCONTOUR", "Contour",
                        "Draws contours at the exact elevations you type, taken off a TIN surface. The surface itself is never changed.")),

                Panel("Line Labels",
                    Large("Label\nLine", "FTFLABELLINE", "LabelLine",
                        "Click a line, slide the preview along it, click to place its label. FTFL for short."),
                    Large("Between", "FTFLABELBETWEEN", "Between",
                        "Labels the surface between two edges, such as a walk between its edges. FTFB for short."),
                    Large("Driveway", "FTFLABELDRIVEWAY", "Driveway",
                        "Labels a driveway between its two edges with its surface type, such as ASPH DWY. FTFDWY for short."),
                    Large("Stairs", "FTFLABELSTAIRS", "Stairs",
                        "Select the stair lines; the label counts the steps for you. FTFS for short.")),

                Panel("Drafting",
                    Large("Draw\nLines", "FTFDRAWLINE", "DrawLine",
                        DrawLineTip),
                    Tool("Break Mark", "FTFTILDE", "Tilde",
                        "Puts a break mark (tilde) across a line where it runs off the view."),
                    Tool("ROW Width", "FTFROWDIM", "RowWidth",
                        RowWidthTip),
                    Tool("Bench", "FTFBENCH", "Bench",
                        "Draws a bench's outline and size label from its two end shots and the depth in their notes."),
                    Tool("Wetland Buffer", "FTFWETLANDBUFFER", "WetlandBuffer",
                        "Offsets wetland lines by the buffer distance with round, tangent corners, and stops the buffer at hard surfaces (road edges, curbs, buildings) between it and the wetland. FTFWLB for short."),
                    Tool("Clean Drafting", "FTFDRAFTCLEAN", "DraftClean",
                        DraftCleanTip)),

                Panel("Sheets",
                    Large("Plan\nSheets", "FTFSHEETPLAN", "PlanSheets",
                        "Proposes the fewest sheets that cover the site at the configured paper size and scale, with match lines. Only the plan is drawn."),
                    Step("Make Layouts", "FTFSHEETMAKE", "MakeLayouts",
                        "Creates one layout per planned sheet, each with a locked viewport. Existing layouts are never overwritten."),
                    Step("From Layouts", "FTFSHEETS", "SheetAreas",
                        "Draws the sheet windows and match lines from layouts that already exist."),
                    Step("Key Map", "FTFKEYMAP", "KeyMap",
                        "Puts the small index map on every sheet, with that sheet's cell drawn heavy."),
                    Step("Match Line", "FTFMATCHLINE", "MatchLine",
                        "Draws the zigzag match line along the seam a viewport cuts. Run it on the layout.")));
        }

        private static RibbonTabLayout Boundary()
        {
            return Tab(BoundaryTabId, "Boundary",
                Panel("Recorded Surveys",
                    Large("Recorded\nSurvey", "FTFRECORD", "Record",
                        "Upload a recorded plat or Record of Survey, review the extracted calls, and build the geometry from them."),
                    Step("Rebuild Record", "FTFRECORDREBUILD", "RecordRebuild",
                        "Reopens a recorded survey's review and rebuilds its geometry after edits."),
                    Step("Record Labels", "FTFRECORDLABEL", "RecordLabel",
                        "Places or refreshes the record labels on the reconstructed lines."),
                    Step("Line Source", "FTFRECORDSOURCE", "RecordSource",
                        "Shows where a reconstructed line came from: its call, its document and its figure's closure.")),

                Panel("Calculations & Closure",
                    Large("Draw\nLines", "FTFDRAWLINE", "DrawLine",
                        DrawLineTip),
                    Large("Check vs\nRecord", "FTFRECORDCHECK", "RecordCheck",
                        "Checks the drawing against the recorded calls: bearings, distances, curves and closure. Flags only; nothing is changed."),
                    Tool("Clean Drafting", "FTFDRAFTCLEAN", "DraftClean",
                        DraftCleanTip)),

                Panel("Right of Way & Lots",
                    Large("Right of\nWay", "FTFROW", "RightOfWay",
                        "Pick the monuments; FTF draws the right-of-way lines, the opposite line, the centerline, width dimensions and monument notes."),
                    Large("Platted\nBlock", "FTFLOTS", "Lots",
                        "Divides a block into lots: lot lines, alley, setbacks, easements, lot numbers, areas and drainage arrows."),
                    Tool("ROW Width", "FTFROWDIM", "RowWidth",
                        RowWidthTip)),

                Panel("Record Comparison",
                    Large("Compare\nSurvey", "FTFCOMPARE", "Compare",
                        "Compares our survey with the consultant's base map: what they already show goes nonplot, what they lack keeps plotting. Opens the review window."),
                    Large("Trim\nAerial", "FTFTRIM", "Trim",
                        "In the aerial base drawing: trims the aerial inside our limits of survey and flags the seams for review."),
                    Step("Run Compare", "FTFCOMPARERUN", "CompareRun",
                        "Compares everything, a selection or a window against the base map, and reports. The command-line twin of the window's Run."),
                    Step("Apply", "FTFCOMPAREAPPLY", "CompareApply",
                        "Applies the approved comparison decisions to the drawing."),
                    Step("Final QC", "FTFCOMPAREQC", "CompareQc",
                        "Runs the final checks on the comparison. A blocking problem stops the deliverable from being saved."),
                    Step("Save Deliverable", "FTFCOMPARESAVE", "CompareSave",
                        "Runs the final QC, then writes the deliverable drawing, its CSV and the QC report to the delivery folder."),
                    Step("Batch Sites", "FTFCOMPAREBATCH", "CompareBatch",
                        "Compares a list of sites one after another. A site with unresolved results goes to a needs-review folder."),
                    Step("Restore Compare", "FTFCOMPARERESTORE", "CompareRestore",
                        "Puts back every object the comparison changed."),
                    Step("Restore Trim", "FTFTRIMRESTORE", "TrimRestore",
                        "Puts back everything Trim Aerial trimmed.")));
        }

        private static RibbonTabLayout Easements()
        {
            return Tab(EasementsTabId, "Easements",
                Panel("Create",
                    Large("Strip\nEasement", "STRIPEASEMENT", "Easement",
                        "Click the angle points and give the width; the strip is trimmed to the lot lines you click. A temporary construction easement can be built alongside."),
                    Large("Portion\nEasement", "PORTIONEASEMENT", "PortionEasement",
                        "An easement over part of a lot, such as the west 10 feet of the south 50 feet, measured square from a picked lot line."),
                    Large("Area\nEasement", "CONSTRUCTIONAREA", "AreaEasement",
                        "A metes-and-bounds area, such as a construction area, clicked corner by corner; a side can follow an existing line or curve."),
                    Step("Exclude Area", "FTFEASEMENTEXCLUDE", "EasementExclude",
                        "Takes an area out of an existing easement (EXCEPT ...) and rebuilds it."),
                    Step("Add Component", "FTFEASEMENTCOMPONENT", "EasementAdd",
                        "Adds another piece to an existing easement (TOGETHER WITH ...) and rebuilds it.")),

                Panel("Legal Descriptions",
                    Large("Legal\nDraft", "FTFEASEMENTLEGAL", "Legal",
                        "Writes a DRAFT legal description from the easement geometry, in the office's Exhibit A pattern, for the surveyor to review."),
                    Step("Export Courses", "FTFEASEMENTEXPORT", "EasementExport",
                        "Writes each easement's ordered courses, width and area beside the drawing: the raw material for a legal description, not one.")),

                Panel("Exhibits",
                    Large("Exhibit", "FTFEXHIBIT", "Exhibit",
                        "Puts the easement on a sheet: the exhibit layout, its viewport, its annotation and its tables."),
                    Step("Rebuild Exhibit", "FTFEXHIBITREBUILD", "ExhibitRebuild",
                        "Rebuilds the exhibit after the easement changed, keeping sheet items you moved and text you edited."),
                    Step("Exhibit Group", "FTFEASEMENTGROUP", "ExhibitGroup",
                        "Puts easements on one exhibit together, such as a permanent easement and its temporary one."),
                    Step("Exhibit QA", "FTFEXHIBITQA", "ExhibitQa",
                        "A short pre-plot review of the exhibit, sorted into errors, items to review and notes. It never approves the exhibit."),
                    Step("Preview PDF", "FTFEXHIBITPREVIEW", "Plot",
                        "Plots the exhibit to a PDF to look at, with the profile's plotting standard."),
                    Step("Stamp", "FTFEXHIBITSTAMP", "Stamp",
                        "Places the stamp block you choose at the exhibit's stamp position; None takes it off again.")),

                Panel("Closure Verification",
                    Large("Inspect\nEasement", "FTFEASEMENTINSPECT", "EasementInspect",
                        "Audits one easement: an independent traverse of its stated courses (misclosure and precision) and where every value came from. Nothing is changed."),
                    Large("Check\nEasements", "FTFEASEMENTCHECK", "EasementCheck",
                        "Checks every stored easement against the survey objects it was built from, and offers to rebuild any whose source changed."),
                    Step("Inspect Exhibit", "FTFEXHIBITINSPECT", "ExhibitInspect",
                        "Audits one exhibit: where each item on the sheet came from. Nothing is changed.")));
        }

        // ============================================== tips shared by two tabs

        private const string DrawLineTip =
            "Drafts boundary, right-of-way, section and easement lines course by course from bearings and distances, annotated to the office standard.";

        private const string RowWidthTip =
            "Click the centerline, then the right-of-way line; the width is written midway between them.";

        private const string DraftCleanTip =
            "Removes the annotation on drafted lines; the lines themselves are erased only after you confirm. Clean Up never touches them.";

        // ================================================================ builders

        private static RibbonTabLayout Tab(string id, string title, params RibbonPanelLayout[] panels)
        {
            return new RibbonTabLayout(id, title, panels);
        }

        private static RibbonPanelLayout Panel(string title, params RibbonButtonLayout[] buttons)
        {
            return new RibbonPanelLayout(title, buttons);
        }

        /// <summary>A large button: one of the panel's main commands.</summary>
        private static RibbonButtonLayout Large(string label, string command, string icon, string tip)
        {
            return new RibbonButtonLayout(label, command, icon, RibbonButtonSize.Large, true, tip);
        }

        /// <summary>A small button that is a workflow of its own.</summary>
        private static RibbonButtonLayout Tool(string label, string command, string icon, string tip)
        {
            return new RibbonButtonLayout(label, command, icon, RibbonButtonSize.Small, true, tip);
        }

        /// <summary>A small button that is a step or variant of its panel's main command.</summary>
        private static RibbonButtonLayout Step(string label, string command, string icon, string tip)
        {
            return new RibbonButtonLayout(label, command, icon, RibbonButtonSize.Small, false, tip);
        }
    }
}
