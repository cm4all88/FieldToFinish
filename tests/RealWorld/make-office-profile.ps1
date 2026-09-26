# Writes the "PMX SURVEY EXHIBIT" FTF profile from the office's own exhibit standard, as measured
# from delivered exhibits and the office templates (see tests\RealWorld\OFFICE-STANDARD.md).
# The generic FTF defaults are not changed; this is a separate named profile.
#   make-office-profile.ps1 [-Out <file>]   (default: config\profiles\PMX SURVEY EXHIBIT.json)
param([string]$Out)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Resolve-Path (Join-Path $here '..\..')
if (-not $Out) { $Out = Join-Path $root 'config\profiles\PMX SURVEY EXHIBIT.json' }
$bin = Join-Path $root 'src\FieldCodes.Cad\bin\Debug\net48'
Add-Type -Path 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\netstandard.dll' -ErrorAction SilentlyContinue
Add-Type -Path (Join-Path $bin 'Newtonsoft.Json.dll')
Add-Type -Path (Join-Path $bin 'FieldCodes.dll')

$s = New-Object FieldCodes.Settings.FtfSettings
$x = $s.Exhibits
$office = 'U:\PSO\Shared\Divisions\00Survey\OFC_RSC\Equipment & Software\Autocad\C3D2024'

# Sheet and plotting: every delivered exhibit (Silver Lake 2026-05, Kenmore 2026-07) and the office template's 8x11 EXHIBIT layout.
$x.SheetWidthIn = 8.5; $x.SheetHeightIn = 11; $x.MarginIn = 1.0
$x.PlotDevice = 'AutoCAD PDF (High Quality Print).pc3'
$x.MediaName = 'ANSI_full_bleed_A_(8.50_x_11.00_Inches)'
$x.PlotStyleTable = 'PMX Survey BW.ctb'
$x.BlockLibrary = Join-Path $office 'Templates\SheetSets\PMX-Survey-Layouts.dwt'
$x.SheetNameFormat = 'EXHIBIT {sheet}'

# Border and title: G-BORD-XLIT rectangle 1.01,1.01 - 7.51,10.01; Border3 (Arial Bold) 0.14 at 4.261,9.857, 6.493 wide,
# "EXHIBIT B" at 1.42857x, then aliquot + county, then the easement type.
$x.DrawBorder = $true
$x.BorderLeftIn = 1.01; $x.BorderBottomIn = 1.01; $x.BorderRightIn = 7.51; $x.BorderTopIn = 10.01
$x.BorderLayer = 'G-BORD-XLIT'
$x.DrawTitle = $true
$x.TitleLines = '{title}|{location}|{county}|{purpose}'   # every reference breaks after W.M.
$x.TitleX = 4.261; $x.TitleY = 9.857; $x.TitleWidthIn = 6.493
$x.TitleTextStyle = 'Border3'; $x.TitleTextHeightIn = 0.14; $x.TitleFirstLineScale = 1.42857; $x.TitleLayer = 'G-BORD-XLIT'
$x.InfoLines = ''
$x.RequiredInfo = 'title;location;county;purpose'

# Office blocks from the template: north arrow and scale bar in one dynamic block, the logo with the office's address.
$x.DrawNorthArrow = $false
$x.DrawScaleBar = $true
$x.ScaleBarBlock = 'G-ScalebarFig'; $x.ScaleBarVisibility = '1" = {scale}''';  $x.ScaleBarX = 4.839; $x.ScaleBarY = 1.399
$x.SheetBlocks = 'PMXLogo@1.054,1.973@G-BORD-LOGO@Visibility1=Puyallup,scale=0.68'
$x.SymbolLayer = 'G-BORD-XLIT'

# Viewport: the reference exhibit's (28052700104100) main viewport, locked, on the no-plot XX-VPRT layer.
# (The Kenmore exhibits use a shorter viewport, 1.043,3.21 6.374 x 5.747 on 00-VP, leaving a band for tables and legend.)
$x.ViewportLeftIn = 1.07; $x.ViewportBottomIn = 1.922; $x.ViewportWidthIn = 6.39; $x.ViewportHeightIn = 6.996
$x.ViewportLayer = 'XX-VPRT'; $x.LockViewport = $true
$x.Scales = '10,20,30,40,50,60,100,200,300,400,500,600,1000'
$x.Orientation = 'NorthUp'
$x.FitMargin = 0.08               # the office exhibits fill the viewport closely (104300 at 1" = 60')
# First match wins. Parcel, ROW, section and control layers have no Hide rule: they stay as context.
# Office exhibits show no topography; the survey base's topo, TIN and point layers are hidden, improvements (V-SURF) only where
# they are a source of the easement (an excluded building, say). V-TEXT-ESMT/FEE/TCE hold other exhibits' labels (Kenmore).
$x.ViewportLayerRules = 'V-ESMT-*=Relevant; V-CTRL-PMX_-PNTS-E=Show; V-CTRL-OTHE-PNTS-E=Hide; *-PNTS-E=Hide; V-TOPO-*=Hide; V-TINN-*=Hide; V-SURF-*=Relevant; C-BNDY-LIMT*=Hide; V-TEXT-ESMT*=Hide; V-TEXT-FEE*=Hide; V-TEXT-TCE*=Hide'

# Annotation: Survey text (romans, 0.75 wide) at 0.08; leaders xPMX SURV Text Arrow Anno; dimensions PMX SURV ANNO.
$x.TextStyle = 'Survey'; $x.TextHeightIn = 0.08
$x.LeaderStyle = 'xPMX SURV Text Arrow Anno'
$x.DimensionStyle = 'PMX SURV ANNO'
$x.AnnotationLayer = 'V-TEXT'; $x.DimensionLayer = 'V-TEXT'; $x.TableLayer = 'V-TEXT-TABL'
$x.AreaLabelTitle = $false
$x.DrawParcelLabel = $false
$x.NarrowStripLabel = 'NoLeader'
$x.StripLabelAlong = $false      # 104100 and 104300: "APPROX EASEMENT AREA = ..." horizontal, beside the strip, no leader

# Tables and legend: below the viewport, above the logo, as on the Kenmore exhibits.
$x.LineTableTitle = 'LINE TABLE'; $x.LineTableColumns = 'LINE NO.={id}|DISTANCE={distance}|BEARING={bearing}'
$x.CurveTableTitle = 'CURVE TABLE'; $x.CurveTableColumns = 'CURVE NO.={id}|LENGTH={length}|RADIUS={radius}|DELTA={delta}'
$x.LineTableX = 1.12; $x.LineTableY = 3.12
$x.DrawLegend = $false; $x.LegendFormat = '{name} ({sqft} SQ. FT.)'; $x.LegendX = 3.76; $x.LegendY = 3.0
$x.AreaTable = 'Never';           # office exhibits state each area on the plan
$x.AreaTableCombined = $false; $x.AreaTableAcres = $false; $x.AreaTableX = 4.6; $x.AreaTableY = 3.12
$x.NotesX = 1.12; $x.NotesY = 2.0; $x.Notes = ''

# Easement drafting in model space: the office's layer and hatch for the permanent easement, its area wording.
$e = $s.Easements
# The V-ESMT family, one sub-layer per kind of object, so an exhibit can show the permanent easement and hide the
# temporary one in its viewport (as the Kenmore exhibits do with their own per-easement layers).
$e.BoundaryLayer = 'V-ESMT-E'; $e.SidelineLayer = 'V-ESMT-E'; $e.CenterlineLayer = 'V-ESMT-CNTR-E'
$e.HatchLayer = 'V-ESMT-PATT-E'
$e.TextLayer = 'V-ESMT-TEXT-E'; $e.DimensionLayer = 'V-ESMT-DIMS-E'; $e.TableLayer = 'V-ESMT-TABL-E'
$e.TemporaryLayer = 'V-ESMT-TEMP-E'; $e.TemporaryHatchLayer = 'V-ESMT-TEMP-PATT-E'
$e.TemporaryTextLayer = 'V-ESMT-TEMP-TEXT-E'; $e.TemporaryDimensionLayer = 'V-ESMT-TEMP-DIMS-E'
$e.AreaLayer = 'V-ESMT-CONS-E'
$e.HatchPattern = 'ANSI31'
# A temporary construction easement is hatched in its own pattern, as the Kenmore TCE is.
$e.TemporaryHatchPattern = 'ANSI37'
# Width dimensions use the office survey dimension style, in model space as well as on the sheet.
$e.DimensionStyleOverride = 'PMX SURV ANNO'

# Recorded surveys (FTFRECORD): the office survey text style. Layers keep FTF's defaults, which are the office
# V-PROP/V-ALGN/V-ESMT names; no PMX label style is named until one is measured from a drawing.
$s.RecordSurvey.TextStyle = 'Survey'
$e.HatchScale = 0.25              # ANSI31 at 0.25 per plotted inch, as the office hatch reads at 1" = 60'
$e.AskTableLocation = $false        # model-space tables are hidden in the exhibit viewport; do not stop to ask where they go
$e.AreaFormat = 'APPROX {purpose} EASEMENT AREA = {sqft} SF'   # reference reads APPROX EASEMENT AREA; the purpose tells a permanent and a temporary easement apart on one sheet

$x.TableColumnScale = 0.65          # Survey text is narrow (0.75 width factor)

# Production cleanup choices (2026-09), each from what the delivered exhibits show -- confirm with the office:
$x.NorthArrowProperty = 'Angle1'    # G-ScalebarFig's rotation parameter turns its north arrow (BLKDUMP of PMX-Survey-Layouts.dwt)
$x.HatchSpacingIn = 0.0625
$x.HatchSpacings = 'ANSI31=0.03125; ANSI37=0.0977'   # 104100: ANSI31 annotative 0.25 prints 1/32"; Kenmore: ANSI37 at 31.25, 1" = 40' prints 0.098"
$x.OverheadPower = 'Hide'; $x.OverheadPowerLayers = 'V-UTIL-POWR-OVHD*'    # 104100 froze V-UTIL-POWR-OVHD-E in its viewport
$x.OtherHatches = 'Relevant'; $x.OtherHatchLayers = 'C-PROP-RWAY-PATT*; C-BNDY-LIMT-PATT*'   # Kenmore froze other exhibits' fee and TCE hatches
$x.StampMode = 'Placeholder'; $x.StampX = 6.512; $x.StampY = 2.06; $x.StampSizeIn = 1.5   # Kenmore's WA-stamp-here place
$x.StampLibrary = Join-Path $office 'Symbols\pmxSurveyStamps.dwg'
$x.AreaLabelFormat = 'APPROX {purpose} EASEMENT AREA = {sqft} SF'
$x.LineTableHeadings = 'DISTANCE/BEARING'     # Silver Lake
$x.TableSpots = ''

New-Item -ItemType Directory -Force (Split-Path -Parent $Out) | Out-Null
$s.Save($Out)
"wrote $Out"

# Second office layout, from the Kenmore exhibits (2026-07): a shorter viewport on 00-VP leaves a band below it
# for the line and curve tables side by side, between the logo, the scale bar and the stamp area.
$x.ViewportLeftIn = 1.043; $x.ViewportBottomIn = 3.21; $x.ViewportWidthIn = 6.374; $x.ViewportHeightIn = 5.747
$x.ViewportLayer = '00-VP'
$x.LineTableX = 1.12; $x.LineTableY = 3.15
$x.CurveTableX = 3.08; $x.CurveTableY = 3.15
$x.DrawLegend = $false
$x.LineTableHeadings = 'LENGTH/DIRECTION'     # Kenmore
$x.TableSpots = '3.08,3.15; 5.0,3.15'         # beside the other table, clear of the logo, when a long table would run into it
$tables = Join-Path (Split-Path -Parent $Out) 'PMX SURVEY EXHIBIT TABLES.json'
$s.Save($tables)
"wrote $tables"
