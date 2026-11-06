namespace FieldCodes.Cad
{
    /// <summary>
    /// Every command name the plugin registers. The internal edition keeps the original names
    /// exactly; the commercial TRACE edition registers TRACE names instead, so it installs and
    /// runs beside the internal edition with no clash. Constants, so they can be used in attributes.
    /// </summary>
    internal static class CommandNames
    {
#if TRACE_EDITION
        public const string CONSTRUCTIONAREA = "TRACECONSTRUCTIONAREA";
        public const string FTF = "TRACEWINDOW";
        public const string FTFAREA = "TRACEAREA";
        public const string FTFB = "TRACEB";
        public const string FTFBLOCKS = "TRACEBLOCKS";
        public const string FTFCLEAN = "TRACECLEAN";
        public const string FTFCONFIG = "TRACECONFIG";
        public const string FTFCONTROL = "TRACECONTROL";
        public const string FTFDIP = "TRACEDIP";
        public const string FTFDIPACT = "TRACEDIPACT";
        public const string FTFDIPCHECK = "TRACEDIPCHECK";
        public const string FTFDIPCONFIRM = "TRACEDIPCONFIRM";
        public const string FTFDIPCONNECT = "TRACEDIPCONNECT";
        public const string FTFDIPDRAW = "TRACEDIPDRAW";
        public const string FTFDIPINSPECT = "TRACEDIPINSPECT";
        public const string FTFDIPLABEL = "TRACEDIPLABEL";
        public const string FTFDIPNOTES = "TRACEDIPNOTES";
        public const string FTFDIPREVISIT = "TRACEDIPREVISIT";
        public const string FTFDRAFTCLEAN = "TRACEDRAFTCLEAN";
        public const string FTFDRAWLINE = "TRACEDRAWLINE";
        public const string FTFDRIP = "TRACEDRIP";
        public const string FTFEASEMENT = "TRACEEASEMENT";
        public const string FTFEASEMENTCHECK = "TRACEEASEMENTCHECK";
        public const string FTFEASEMENTCOMPONENT = "TRACEEASEMENTCOMPONENT";
        public const string FTFEASEMENTEXCLUDE = "TRACEEASEMENTEXCLUDE";
        public const string FTFEASEMENTEXPORT = "TRACEEASEMENTEXPORT";
        public const string FTFEASEMENTGROUP = "TRACEEASEMENTGROUP";
        public const string FTFEASEMENTINSPECT = "TRACEEASEMENTINSPECT";
        public const string FTFEASEMENTLEGAL = "TRACEEASEMENTLEGAL";
        public const string FTFEXHIBIT = "TRACEEXHIBIT";
        public const string FTFEXHIBITINSPECT = "TRACEEXHIBITINSPECT";
        public const string FTFEXHIBITPREVIEW = "TRACEEXHIBITPREVIEW";
        public const string FTFEXHIBITQA = "TRACEEXHIBITQA";
        public const string FTFEXHIBITREBUILD = "TRACEEXHIBITREBUILD";
        public const string FTFEXHIBITSTAMP = "TRACEEXHIBITSTAMP";
        public const string FTFKEYMAP = "TRACEKEYMAP";
        public const string FTFL = "TRACEL";
        public const string FTFLABELBETWEEN = "TRACELABELBETWEEN";
        public const string FTFLABELLINE = "TRACELABELLINE";
        public const string FTFLABELS = "TRACELABELS";
        public const string FTFLABELSTAIRS = "TRACELABELSTAIRS";
        public const string FTFLEGEND = "TRACELEGEND";
        public const string FTFLINELABELS = "TRACELINELABELS";
        public const string FTFLINEMETA = "TRACELINEMETA";
        public const string FTFLINES = "TRACELINES";
        public const string FTFOPTIONS = "TRACEOPTIONS";
        public const string FTFORDER = "TRACEORDER";
        public const string FTFPOINTS = "TRACEPOINTS";
        public const string FTFPORTION = "TRACEPORTION";
        public const string FTFPROFILE = "TRACEPROFILE";
        public const string FTFRUN = "TRACERUNALL";
        public const string FTFS = "TRACES";
        public const string FTFSETTINGS = "TRACESETTINGS";
        public const string FTFSETUP = "TRACESETUP";
        public const string FTFSHEETMAKE = "TRACESHEETMAKE";
        public const string FTFSHEETPLAN = "TRACESHEETPLAN";
        public const string FTFSHEETS = "TRACESHEETS";
        public const string FTFSPOT = "TRACESPOT";
        public const string FTFTABLE = "TRACETABLE";
        public const string FTFTAGS = "TRACETAGS";
        public const string FTFTREES = "TRACETREES";
        public const string FTFWHERE = "TRACEWHERE";
        public const string PORTIONEASEMENT = "TRACEPORTIONEASEMENT";
        public const string STRIPEASEMENT = "TRACESTRIPEASEMENT";
#else
        public const string CONSTRUCTIONAREA = "CONSTRUCTIONAREA";
        public const string FTF = "FTF";
        public const string FTFAREA = "FTFAREA";
        public const string FTFB = "FTFB";
        public const string FTFBLOCKS = "FTFBLOCKS";
        public const string FTFCLEAN = "FTFCLEAN";
        public const string FTFCONFIG = "FTFCONFIG";
        public const string FTFCONTROL = "FTFCONTROL";
        public const string FTFDIP = "FTFDIP";
        public const string FTFDIPACT = "FTFDIPACT";
        public const string FTFDIPCHECK = "FTFDIPCHECK";
        public const string FTFDIPCONFIRM = "FTFDIPCONFIRM";
        public const string FTFDIPCONNECT = "FTFDIPCONNECT";
        public const string FTFDIPDRAW = "FTFDIPDRAW";
        public const string FTFDIPINSPECT = "FTFDIPINSPECT";
        public const string FTFDIPLABEL = "FTFDIPLABEL";
        public const string FTFDIPNOTES = "FTFDIPNOTES";
        public const string FTFDIPREVISIT = "FTFDIPREVISIT";
        public const string FTFDRAFTCLEAN = "FTFDRAFTCLEAN";
        public const string FTFDRAWLINE = "FTFDRAWLINE";
        public const string FTFDRIP = "FTFDRIP";
        public const string FTFEASEMENT = "FTFEASEMENT";
        public const string FTFEASEMENTCHECK = "FTFEASEMENTCHECK";
        public const string FTFEASEMENTCOMPONENT = "FTFEASEMENTCOMPONENT";
        public const string FTFEASEMENTEXCLUDE = "FTFEASEMENTEXCLUDE";
        public const string FTFEASEMENTEXPORT = "FTFEASEMENTEXPORT";
        public const string FTFEASEMENTGROUP = "FTFEASEMENTGROUP";
        public const string FTFEASEMENTINSPECT = "FTFEASEMENTINSPECT";
        public const string FTFEASEMENTLEGAL = "FTFEASEMENTLEGAL";
        public const string FTFEXHIBIT = "FTFEXHIBIT";
        public const string FTFEXHIBITINSPECT = "FTFEXHIBITINSPECT";
        public const string FTFEXHIBITPREVIEW = "FTFEXHIBITPREVIEW";
        public const string FTFEXHIBITQA = "FTFEXHIBITQA";
        public const string FTFEXHIBITREBUILD = "FTFEXHIBITREBUILD";
        public const string FTFEXHIBITSTAMP = "FTFEXHIBITSTAMP";
        public const string FTFKEYMAP = "FTFKEYMAP";
        public const string FTFL = "FTFL";
        public const string FTFLABELBETWEEN = "FTFLABELBETWEEN";
        public const string FTFLABELLINE = "FTFLABELLINE";
        public const string FTFLABELS = "FTFLABELS";
        public const string FTFLABELSTAIRS = "FTFLABELSTAIRS";
        public const string FTFLEGEND = "FTFLEGEND";
        public const string FTFLINELABELS = "FTFLINELABELS";
        public const string FTFLINEMETA = "FTFLINEMETA";
        public const string FTFLINES = "FTFLINES";
        public const string FTFOPTIONS = "FTFOPTIONS";
        public const string FTFORDER = "FTFORDER";
        public const string FTFPOINTS = "FTFPOINTS";
        public const string FTFPORTION = "FTFPORTION";
        public const string FTFPROFILE = "FTFPROFILE";
        public const string FTFRUN = "FTFRUN";
        public const string FTFS = "FTFS";
        public const string FTFSETTINGS = "FTFSETTINGS";
        public const string FTFSETUP = "FTFSETUP";
        public const string FTFSHEETMAKE = "FTFSHEETMAKE";
        public const string FTFSHEETPLAN = "FTFSHEETPLAN";
        public const string FTFSHEETS = "FTFSHEETS";
        public const string FTFSPOT = "FTFSPOT";
        public const string FTFTABLE = "FTFTABLE";
        public const string FTFTAGS = "FTFTAGS";
        public const string FTFTREES = "FTFTREES";
        public const string FTFWHERE = "FTFWHERE";
        public const string PORTIONEASEMENT = "PORTIONEASEMENT";
        public const string STRIPEASEMENT = "STRIPEASEMENT";
#endif
    }
}
