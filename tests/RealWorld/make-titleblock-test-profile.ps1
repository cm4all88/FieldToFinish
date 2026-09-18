# A test-only profile (not shipped): the office 22x34 survey border as a title block with attributes, to test
# FTF's attribute mapping against a real office title block. Written to %APPDATA%\FieldToFinish\profiles.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Resolve-Path (Join-Path $here '..\..')
$bin = Join-Path $root 'src\FieldCodes.Cad\bin\Debug\net48'
Add-Type -Path 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\netstandard.dll' -ErrorAction SilentlyContinue
Add-Type -Path (Join-Path $bin 'Newtonsoft.Json.dll')
Add-Type -Path (Join-Path $bin 'FieldCodes.dll')
$s = [FieldCodes.Settings.FtfSettings]::Load((Join-Path $root 'config\profiles\PMX SURVEY EXHIBIT.json'))
$x = $s.Exhibits
$x.SheetWidthIn = 34; $x.SheetHeightIn = 22; $x.MarginIn = 0.5
$x.MediaName = 'ANSI_full_bleed_D_(34.00_x_22.00_Inches)'
$x.DrawBorder = $false
$x.TitleBlockName = 'PMXSurv22X34Border'; $x.TitleBlockX = 0; $x.TitleBlockY = 0
# Mapped: the office's own tags. DesignedBy and ApprovedBy are deliberately not mapped: FTF must leave them alone and say so.
$x.TitleBlockAttributes = 'JobNo={projectNumber}; SubmitDate={date}; SheetNo={sheetNo}; SheetOf={sheetOf}; DrawnBy={preparedBy}; CheckedBy={checkedBy}'
$x.ViewportLeftIn = 2; $x.ViewportBottomIn = 3; $x.ViewportWidthIn = 24; $x.ViewportHeightIn = 16
$x.TitleX = 14; $x.TitleY = 20.5; $x.TitleWidthIn = 20
$x.ScaleBarX = 26; $x.ScaleBarY = 4; $x.SheetBlocks = ''
$x.LineTableX = 27; $x.LineTableY = 18
$out = Join-Path $env:APPDATA 'FieldToFinish\profiles\PMX TITLEBLOCK TEST.json'
$s.Save($out)
"wrote $out"
