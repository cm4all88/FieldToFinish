using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FieldCodes.Utilities.FieldNotes
{
    // =====================================================================
    // Pages as a list of drawing operations, in points (1/72 inch) with the
    // origin at the TOP-left and y running down the page, the way a page is
    // read. The PDF writer turns these into PDF operators; tests read them
    // directly to check what is on the page and in which colour.
    // =====================================================================

    /// <summary>The four standard PDF fonts the field notes use. Every PDF reader has them; nothing is embedded.</summary>
    public enum PageFont { Helvetica, HelveticaBold, HelveticaOblique, Courier }

    /// <summary>An RGB colour, 0..1 per channel.</summary>
    public struct PageColor : IEquatable<PageColor>
    {
        public readonly double R, G, B;

        public PageColor(double r, double g, double b) { R = r; G = g; B = b; }

        /// <summary>Measured and recorded values, descriptions, notes.</summary>
        public static readonly PageColor Black = new PageColor(0, 0, 0);

        /// <summary>Calculated elevations. Pure red: it stands out on screen and in colour print.</summary>
        public static readonly PageColor Red = new PageColor(1, 0, 0);

        /// <summary>Rules and the sketch outline.</summary>
        public static readonly PageColor Gray = new PageColor(0.45, 0.45, 0.45);

        /// <summary>The faint field-book grid behind a sketch.</summary>
        public static readonly PageColor Grid = new PageColor(0.84, 0.87, 0.90);

        public static readonly PageColor White = new PageColor(1, 1, 1);

        public bool Equals(PageColor other) { return R == other.R && G == other.G && B == other.B; }
        public override bool Equals(object obj) { return obj is PageColor && Equals((PageColor)obj); }
        public override int GetHashCode() { return R.GetHashCode() ^ (G.GetHashCode() * 31) ^ (B.GetHashCode() * 17); }

        public override string ToString()
        {
            return PageNumbers.Format(R) + " " + PageNumbers.Format(G) + " " + PageNumbers.Format(B);
        }
    }

    /// <summary>What a piece of text IS, so tests (and readers of the code) can tell a measured depth from a
    /// calculated elevation without parsing the words.</summary>
    public enum TextRole
    {
        /// <summary>Headings, captions, column titles, page furniture.</summary>
        Chrome,
        /// <summary>Recorded or surveyed facts: structure info, sizes, directions, notes.</summary>
        Recorded,
        /// <summary>A measured depth below the rim, as recorded (MD).</summary>
        MeasuredDepth,
        /// <summary>A calculated pipe invert elevation (INV).</summary>
        CalculatedInvert,
        /// <summary>Any other calculated elevation: top of pipe, springline, bottom, water surface.</summary>
        CalculatedElevation,
        /// <summary>A warning or qualification.</summary>
        Warning,
        /// <summary>Text that changes every time the PDF is saved (issue time, revision); left out of the
        /// content fingerprint so an unchanged record is recognised as unchanged.</summary>
        Volatile
    }

    public abstract class PageOp { }

    /// <summary>A run of text. X is the left end, Y the baseline.</summary>
    public sealed class TextOp : PageOp
    {
        public double X { get; set; }
        public double Y { get; set; }
        public PageFont Font { get; set; }
        public double Size { get; set; }
        public PageColor Color { get; set; }
        public string Text { get; set; }
        public TextRole Role { get; set; }

        public override string ToString() { return Text; }
    }

    public sealed class LineOp : PageOp
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double Width { get; set; }
        public PageColor Color { get; set; }
        /// <summary>Dash pattern (on, off) in points; null for a solid line.</summary>
        public double[] Dash { get; set; }
    }

    public sealed class RectOp : PageOp
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        public double Width { get; set; }
        public PageColor? Stroke { get; set; }
        public PageColor? Fill { get; set; }
    }

    public sealed class CircleOp : PageOp
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Radius { get; set; }
        public double Width { get; set; }
        public PageColor? Stroke { get; set; }
        public PageColor? Fill { get; set; }
    }

    /// <summary>A closed, filled polygon (arrowheads).</summary>
    public sealed class PolygonOp : PageOp
    {
        public IList<double> Points { get; private set; }
        public PageColor Fill { get; set; }

        public PolygonOp() { Points = new List<double>(); }
    }

    public sealed class LayoutPage
    {
        public IList<PageOp> Ops { get; private set; }
        public LayoutPage() { Ops = new List<PageOp>(); }

        public IEnumerable<TextOp> Texts
        {
            get { foreach (var op in Ops) { var t = op as TextOp; if (t != null) yield return t; } }
        }
    }

    /// <summary>A laid-out document: US Letter pages of drawing operations.</summary>
    public sealed class LayoutDocument
    {
        public const double LetterWidth = 612;
        public const double LetterHeight = 792;

        public double Width { get; set; }
        public double Height { get; set; }
        public IList<LayoutPage> Pages { get; private set; }

        public LayoutDocument()
        {
            Width = LetterWidth;
            Height = LetterHeight;
            Pages = new List<LayoutPage>();
        }

        public IEnumerable<TextOp> Texts
        {
            get { foreach (var p in Pages) foreach (var t in p.Texts) yield return t; }
        }

        /// <summary>
        /// A hash of everything the record says -- every text in order with its font and colour -- leaving out the
        /// issue time and revision. Two saves of the same observations give the same fingerprint, so an automatic
        /// save can tell nothing has changed.
        /// </summary>
        public string Fingerprint()
        {
            var sb = new StringBuilder();
            foreach (var t in Texts)
            {
                if (t.Role == TextRole.Volatile) continue;
                sb.Append(t.Font).Append('|').Append(t.Color).Append('|').Append(t.Text).Append('\n');
            }
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }
    }

    internal static class PageNumbers
    {
        /// <summary>A coordinate or size for a PDF operator: up to three decimals, invariant, never "-0".</summary>
        public static string Format(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0;
            var rounded = Math.Round(value, 3);
            if (rounded == 0) return "0";
            return rounded.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Text widths for the standard PDF fonts (Adobe's published AFM metrics, WinAnsi encoding), so lines can be
    /// wrapped and numbers right-aligned without a font file. Characters outside WinAnsi are written as '?'.
    /// </summary>
    public static class FontMetrics
    {
        // Widths in 1/1000 em for characters 32..126.
        private static readonly short[] Helvetica =
        {
            278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
            1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
            333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
            556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584
        };

        private static readonly short[] HelveticaBold =
        {
            278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
            975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
            333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
            611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584
        };

        /// <summary>The width of the text in points.</summary>
        public static double Width(PageFont font, double size, string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var bytes = WinAnsi.Encode(text);
            double units = 0;
            foreach (var b in bytes) units += CharWidth(font, b);
            return units * size / 1000.0;
        }

        private static double CharWidth(PageFont font, byte c)
        {
            if (font == PageFont.Courier) return 600;
            var table = font == PageFont.HelveticaBold ? HelveticaBold : Helvetica;
            if (c >= 32 && c <= 126) return table[c - 32];
            switch (c)
            {
                case 0xB0: return 400;                                           // degree
                case 0x96: return 556;                                           // en dash
                case 0x97: return 1000;                                          // em dash
                case 0x95: return 350;                                           // bullet
                case 0xB7: return 278;                                           // middle dot
                case 0xD7: return 584;                                           // multiply
                case 0xB1: return 584;                                           // plus-minus
                case 0x86: return 556;                                           // dagger
                case 0x91: case 0x92: return font == PageFont.HelveticaBold ? 278 : 222;
                case 0x93: case 0x94: return font == PageFont.HelveticaBold ? 500 : 333;
                default: return 556;
            }
        }

        /// <summary>Breaks text into lines no wider than maxWidth, at spaces where it can and mid-word where it must.</summary>
        public static IList<string> Wrap(string text, PageFont font, double size, double maxWidth)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
            {
                var words = paragraph.Split(' ');
                var line = new StringBuilder();
                foreach (var raw in words)
                {
                    var word = raw;
                    var candidate = line.Length == 0 ? word : line + " " + word;
                    if (Width(font, size, candidate) <= maxWidth)
                    {
                        line.Clear().Append(candidate);
                        continue;
                    }
                    if (line.Length > 0)
                    {
                        lines.Add(line.ToString());
                        line.Clear();
                    }
                    // A single word wider than the line is split where it has to be.
                    while (word.Length > 1 && Width(font, size, word) > maxWidth)
                    {
                        var n = word.Length - 1;
                        while (n > 1 && Width(font, size, word.Substring(0, n)) > maxWidth) n--;
                        lines.Add(word.Substring(0, n));
                        word = word.Substring(n);
                    }
                    line.Append(word);
                }
                lines.Add(line.ToString());
            }
            return lines;
        }
    }

    /// <summary>
    /// Text to WinAnsi (Windows-1252) bytes, the encoding the PDF fonts are declared with. Latin-1 maps straight
    /// across; the common typographic marks map to their 1252 codes; anything else becomes '?'.
    /// </summary>
    public static class WinAnsi
    {
        public static byte[] Encode(string text)
        {
            if (string.IsNullOrEmpty(text)) return new byte[0];
            var bytes = new List<byte>(text.Length);
            foreach (var ch in text)
            {
                if (ch == '\t') { bytes.Add(32); continue; }
                if (ch >= 32 && ch <= 126) { bytes.Add((byte)ch); continue; }
                if (ch >= 0xA0 && ch <= 0xFF) { bytes.Add((byte)ch); continue; }
                switch (ch)
                {
                    case '–': bytes.Add(0x96); break;   // en dash
                    case '—': bytes.Add(0x97); break;   // em dash
                    case '•': bytes.Add(0x95); break;   // bullet
                    case '‘': bytes.Add(0x91); break;
                    case '’': bytes.Add(0x92); break;
                    case '“': bytes.Add(0x93); break;
                    case '”': bytes.Add(0x94); break;
                    case '†': bytes.Add(0x86); break;   // dagger
                    case '…': bytes.Add(0x85); break;   // ellipsis
                    case '€': bytes.Add(0x80); break;   // euro
                    case '→': bytes.Add((byte)'>'); break;   // arrow: not in WinAnsi
                    case '−': bytes.Add((byte)'-'); break;   // minus sign
                    default: bytes.Add((byte)'?'); break;
                }
            }
            return bytes.ToArray();
        }
    }
}
