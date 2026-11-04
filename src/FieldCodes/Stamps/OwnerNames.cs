using System;
using System.Collections.Generic;

namespace FieldCodes.Stamps
{
    /// <summary>
    /// What made each kind of object FTF stamps, in a drafter's words: the command, and what the
    /// object is. FTFWHAT reads it, so a drafter can tell any FTF object from a Civil 3D one or
    /// one drawn by hand. Keyed by the stamp kind's name (FtfEntityKind in the plugin); a test
    /// keeps every kind named here.
    /// </summary>
    public static class OwnerNames
    {
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Unknown", "an FTF object of an unknown kind" },
            { "Block", "FTFPOINTS: a symbol block inserted at a point" },
            { "DripArc", "FTFDRIP: a tree drip line" },
            { "Label", "FTFLABELS / FTFLABELALL: a point label" },
            { "Mask", "FTFLABELS / FTFLABELALL: the mask under a point label" },
            { "Leader", "FTFLABELS / FTFLABELALL: a point label's leader" },
            { "Tag", "FTFTAGS: a tag on the plan" },
            { "Table", "FTFTAGS: the tag schedule" },
            { "LineLabel", "FTFLINELABELS: a label along linework" },
            { "LineMask", "FTFLINELABELS: the mask under a line label" },
            { "DraftLine", "FTFDRAWLINE: a drafted survey line (your work; FTFCLEAN leaves it)" },
            { "DraftAnnotation", "FTFDRAWLINE: a drafted course's bearing and distance" },
            { "DraftMask", "FTFDRAWLINE: the mask under drafted-course text" },
            { "SheetArea", "FTFSHEETS: a sheet's plot area" },
            { "MatchLine", "FTFSHEETS: a match line" },
            { "KeyMap", "FTFSHEETS: the key map" },
            { "Spot", "FTFSPOT: a spot elevation" },
            { "Legend", "FTFLEGEND: the line legend" },
            { "ControlTable", "FTFCONTROL: the control point table" },
            { "UtilityPipe", "FTFDIP: a utility pipe drawn from dip data" },
            { "UtilityPipeLabel", "FTFDIP: a pipe label" },
            { "StructureLabel", "FTFDIP: a structure label" },
            { "EasementBoundary", "FTFEASEMENT: an easement boundary" },
            { "EasementLine", "FTFEASEMENT: an easement line" },
            { "EasementHatch", "FTFEASEMENT: an easement hatch" },
            { "EasementDimension", "FTFEASEMENT: an easement dimension" },
            { "EasementText", "FTFEASEMENT: easement text" },
            { "EasementTable", "FTFEASEMENT: an easement table" },
            { "ExhibitViewport", "FTFEXHIBIT: an exhibit viewport" },
            { "ExhibitText", "FTFEXHIBIT: exhibit text" },
            { "ExhibitTable", "FTFEXHIBIT: an exhibit table" },
            { "ExhibitSymbol", "FTFEXHIBIT: an exhibit symbol" },
            { "ExhibitLabel", "FTFEXHIBIT: an exhibit label" },
            { "ExhibitBorder", "FTFEXHIBIT: the exhibit border" },
            { "ExhibitDimension", "FTFEXHIBIT: an exhibit dimension" },
            { "RecordLine", "FTFRECORD: a record line" },
            { "RecordCurve", "FTFRECORD: a record curve" },
            { "RecordLabel", "FTFRECORD: a record label" },
            { "RecordMask", "FTFRECORD: the mask under a record label" },
            { "RecordMonument", "FTFRECORD: a record monument" },
            { "RecordTable", "FTFRECORD: a record table" },
            { "RecordText", "FTFRECORD: record text" },
            { "BreakMark", "FTFTILDE / FTFMATCHLINE: a break mark" },
            { "BreakLine", "FTFTILDE / FTFMATCHLINE: a break line" },
            { "RowWidth", "FTFROWDIM: a right-of-way width" },
            { "CompareSplit", "FTFCOMPARE: a comparison split (QC)" },
            { "CompareCloud", "FTFCOMPARE: a comparison cloud (QC)" },
            { "TrimSplit", "FTFTRIM: a trim split (QC)" },
            { "TrimCloud", "FTFTRIM: a trim cloud (QC)" },
            { "Contour", "FTFCONTOUR: a contour" },
            { "ContourLabel", "FTFCONTOUR: a contour label" },
            { "ContourMask", "FTFCONTOUR: the mask under a contour label" },
            { "OrientQc", "FTFORIENT: review graphics (not plotted; FTFORIENT and FTFORIENTRESET remove them)" },
            { "LineGapProposal", "point accountability (linework continuity): a proposed join, for review" },
            { "LineCorrection", "point accountability (linework continuity): an accepted correction" },
            { "WetlandBuffer", "FTFWETLANDBUFFER: a wetland buffer line (your drafting; FTFCLEAN leaves it; the command run again on the same wetland replaces it)" },
        };

        /// <summary>The command and what the object is, for a stamp kind's name.</summary>
        public static string For(string kindName)
        {
            string name;
            if (!string.IsNullOrWhiteSpace(kindName) && Names.TryGetValue(kindName.Trim(), out name)) return name;
            return "an FTF object (" + (kindName ?? "no kind") + ")";
        }

        /// <summary>True when the kind is named here (a test keeps every kind named).</summary>
        public static bool Knows(string kindName)
        {
            return !string.IsNullOrWhiteSpace(kindName) && Names.ContainsKey(kindName.Trim());
        }
    }
}
