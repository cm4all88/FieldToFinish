using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FieldCodes.Utilities.FieldNotes
{
    /// <summary>Document information written into the PDF.</summary>
    public sealed class PdfMetadata
    {
        public string Title { get; set; }
        public string Subject { get; set; }
        public string Keywords { get; set; }
        public string Author { get; set; }
        public DateTimeOffset Created { get; set; }

        /// <summary>The content fingerprint (<see cref="LayoutDocument.Fingerprint"/>), stored under
        /// /FTFContentHash so a later save can tell whether anything changed.</summary>
        public string ContentHash { get; set; }

        public PdfMetadata() { Created = DateTimeOffset.Now; }
    }

    /// <summary>
    /// A minimal PDF 1.4 writer: one catalog, one page tree, the standard Helvetica / Helvetica-Bold /
    /// Helvetica-Oblique / Courier fonts (never embedded; every PDF reader has them), one content stream per page
    /// and a classic cross-reference table with exact byte offsets. No printer driver, no third-party library:
    /// the bytes are written here. Content streams are Flate-compressed (zlib, via DeflateStream) unless asked not
    /// to be; the document information dictionary is always plain text.
    /// </summary>
    public static class PdfWriter
    {
        public const string ContentHashKey = "FTFContentHash";

        private const int CatalogObject = 1;
        private const int PagesObject = 2;
        private const int FirstFontObject = 3;   // 3..6: F1 Helvetica, F2 Bold, F3 Oblique, F4 Courier
        private const int InfoObject = 7;
        private const int FirstPageObject = 8;

        private static readonly string[] FontNames = { "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Courier" };

        public static byte[] Write(LayoutDocument document, PdfMetadata metadata, bool compress)
        {
            if (document == null) throw new ArgumentNullException("document");
            metadata = metadata ?? new PdfMetadata();
            var pages = document.Pages.Count > 0 ? document.Pages : new List<LayoutPage> { new LayoutPage() };

            using (var output = new MemoryStream())
            {
                var offsets = new SortedDictionary<int, long>();
                // Header, then a comment of four bytes above 127 so transfer tools treat the file as binary.
                Ascii(output, "%PDF-1.4\n");
                output.Write(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' }, 0, 6);

                var kids = new StringBuilder();
                for (var i = 0; i < pages.Count; i++)
                    kids.Append(i == 0 ? string.Empty : " ").Append(FirstPageObject + 2 * i).Append(" 0 R");

                BeginObject(output, offsets, CatalogObject);
                Ascii(output, "<< /Type /Catalog /Pages " + PagesObject + " 0 R >>\n");
                EndObject(output);

                BeginObject(output, offsets, PagesObject);
                Ascii(output, "<< /Type /Pages /Kids [" + kids + "] /Count " + pages.Count.ToString(CultureInfo.InvariantCulture) + " >>\n");
                EndObject(output);

                for (var f = 0; f < FontNames.Length; f++)
                {
                    BeginObject(output, offsets, FirstFontObject + f);
                    Ascii(output, "<< /Type /Font /Subtype /Type1 /BaseFont /" + FontNames[f] + " /Encoding /WinAnsiEncoding >>\n");
                    EndObject(output);
                }

                BeginObject(output, offsets, InfoObject);
                Ascii(output, InfoDictionary(metadata) + "\n");
                EndObject(output);

                var fontResources = "<< /F1 " + FirstFontObject + " 0 R /F2 " + (FirstFontObject + 1) + " 0 R /F3 " +
                                    (FirstFontObject + 2) + " 0 R /F4 " + (FirstFontObject + 3) + " 0 R >>";
                var mediaBox = "[0 0 " + PageNumbers.Format(document.Width) + " " + PageNumbers.Format(document.Height) + "]";
                for (var i = 0; i < pages.Count; i++)
                {
                    var pageObject = FirstPageObject + 2 * i;
                    var contentObject = pageObject + 1;

                    BeginObject(output, offsets, pageObject);
                    Ascii(output, "<< /Type /Page /Parent " + PagesObject + " 0 R /MediaBox " + mediaBox +
                                  " /Resources << /Font " + fontResources + " /ProcSet [/PDF /Text] >> /Contents " +
                                  contentObject + " 0 R >>\n");
                    EndObject(output);

                    var content = ContentStream(pages[i], document.Height);
                    var data = compress ? Zlib(content) : content;
                    BeginObject(output, offsets, contentObject);
                    Ascii(output, "<< /Length " + data.Length.ToString(CultureInfo.InvariantCulture) +
                                  (compress ? " /Filter /FlateDecode" : string.Empty) + " >>\nstream\n");
                    output.Write(data, 0, data.Length);
                    Ascii(output, "\nendstream\n");
                    EndObject(output);
                }

                var size = FirstPageObject + 2 * pages.Count;
                var xref = output.Position;
                Ascii(output, "xref\n0 " + size.ToString(CultureInfo.InvariantCulture) + "\n");
                Ascii(output, "0000000000 65535 f \n");
                for (var n = 1; n < size; n++)
                    Ascii(output, offsets[n].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");

                var id = Id(metadata);
                Ascii(output, "trailer\n<< /Size " + size.ToString(CultureInfo.InvariantCulture) + " /Root " + CatalogObject +
                              " 0 R /Info " + InfoObject + " 0 R /ID [<" + id + "> <" + id + ">] >>\nstartxref\n" +
                              xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
                return output.ToArray();
            }
        }

        // ---------------------------------------------------------------- objects

        private static void BeginObject(Stream output, IDictionary<int, long> offsets, int number)
        {
            offsets[number] = output.Position;
            Ascii(output, number.ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
        }

        private static void EndObject(Stream output)
        {
            Ascii(output, "endobj\n");
        }

        private static void Ascii(Stream output, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            output.Write(bytes, 0, bytes.Length);
        }

        private static string InfoDictionary(PdfMetadata m)
        {
            var sb = new StringBuilder("<< ");
            if (!string.IsNullOrEmpty(m.Title)) sb.Append("/Title ").Append(Literal(m.Title)).Append(' ');
            if (!string.IsNullOrEmpty(m.Subject)) sb.Append("/Subject ").Append(Literal(m.Subject)).Append(' ');
            if (!string.IsNullOrEmpty(m.Keywords)) sb.Append("/Keywords ").Append(Literal(m.Keywords)).Append(' ');
            if (!string.IsNullOrEmpty(m.Author)) sb.Append("/Author ").Append(Literal(m.Author)).Append(' ');
            sb.Append("/Creator (FieldToFinish Dip Builder) /Producer (FieldToFinish) ");
            sb.Append("/CreationDate ").Append(Literal(PdfDate(m.Created))).Append(' ');
            if (!string.IsNullOrEmpty(m.ContentHash)) sb.Append('/').Append(ContentHashKey).Append(' ').Append(Literal(m.ContentHash)).Append(' ');
            sb.Append(">>");
            return sb.ToString();
        }

        /// <summary>D:YYYYMMDDHHmmSS+HH'mm' as the PDF reference defines it.</summary>
        public static string PdfDate(DateTimeOffset when)
        {
            var offset = when.Offset;
            var sign = offset < TimeSpan.Zero ? "-" : "+";
            var abs = offset.Duration();
            return "D:" + when.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + sign +
                   abs.Hours.ToString("00", CultureInfo.InvariantCulture) + "'" +
                   abs.Minutes.ToString("00", CultureInfo.InvariantCulture) + "'";
        }

        private static string Id(PdfMetadata m)
        {
            var seed = (m.ContentHash ?? string.Empty) + "|" + PdfDate(m.Created) + "|" + (m.Title ?? string.Empty);
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
                var hex = new StringBuilder();
                foreach (var b in hash) hex.Append(b.ToString("X2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        /// <summary>A PDF literal string: parentheses and backslashes escaped, anything outside printable ASCII
        /// written as an octal escape of its WinAnsi code.</summary>
        public static string Literal(string text)
        {
            var sb = new StringBuilder("(");
            foreach (var b in WinAnsi.Encode(text ?? string.Empty))
            {
                if (b == (byte)'(' || b == (byte)')' || b == (byte)'\\') sb.Append('\\').Append((char)b);
                else if (b < 32 || b > 126) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                else sb.Append((char)b);
            }
            return sb.Append(')').ToString();
        }

        // ---------------------------------------------------------------- content

        /// <summary>The page's drawing operators. Layout y runs down from the top; PDF y runs up from the bottom.</summary>
        internal static byte[] ContentStream(LayoutPage page, double pageHeight)
        {
            var sb = new StringBuilder();
            sb.Append("1 J 1 j\n");   // round caps and joins: lines and outlines read cleanly when printed
            foreach (var op in page.Ops)
            {
                var text = op as TextOp;
                if (text != null)
                {
                    if (string.IsNullOrEmpty(text.Text)) continue;
                    sb.Append("BT /").Append(FontResource(text.Font)).Append(' ').Append(F(text.Size)).Append(" Tf ")
                      .Append(text.Color).Append(" rg ")
                      .Append(F(text.X)).Append(' ').Append(F(pageHeight - text.Y)).Append(" Td ")
                      .Append(Literal(text.Text)).Append(" Tj ET\n");
                    continue;
                }

                var line = op as LineOp;
                if (line != null)
                {
                    sb.Append("q ").Append(F(line.Width)).Append(" w ").Append(line.Color).Append(" RG ")
                      .Append(Dash(line.Dash)).Append(' ')
                      .Append(F(line.X1)).Append(' ').Append(F(pageHeight - line.Y1)).Append(" m ")
                      .Append(F(line.X2)).Append(' ').Append(F(pageHeight - line.Y2)).Append(" l S Q\n");
                    continue;
                }

                var rect = op as RectOp;
                if (rect != null)
                {
                    sb.Append("q ").Append(F(rect.Width)).Append(" w ");
                    if (rect.Fill.HasValue) sb.Append(rect.Fill.Value).Append(" rg ");
                    if (rect.Stroke.HasValue) sb.Append(rect.Stroke.Value).Append(" RG ");
                    sb.Append(F(rect.X)).Append(' ').Append(F(pageHeight - rect.Y - rect.H)).Append(' ')
                      .Append(F(rect.W)).Append(' ').Append(F(rect.H)).Append(" re ")
                      .Append(Paint(rect.Stroke.HasValue, rect.Fill.HasValue)).Append(" Q\n");
                    continue;
                }

                var circle = op as CircleOp;
                if (circle != null)
                {
                    sb.Append("q ").Append(F(circle.Width)).Append(" w ");
                    if (circle.Fill.HasValue) sb.Append(circle.Fill.Value).Append(" rg ");
                    if (circle.Stroke.HasValue) sb.Append(circle.Stroke.Value).Append(" RG ");
                    Circle(sb, circle.X, pageHeight - circle.Y, circle.Radius);
                    sb.Append(Paint(circle.Stroke.HasValue, circle.Fill.HasValue)).Append(" Q\n");
                    continue;
                }

                var polygon = op as PolygonOp;
                if (polygon != null && polygon.Points.Count >= 6)
                {
                    sb.Append("q ").Append(polygon.Fill).Append(" rg ");
                    for (var i = 0; i + 1 < polygon.Points.Count; i += 2)
                        sb.Append(F(polygon.Points[i])).Append(' ').Append(F(pageHeight - polygon.Points[i + 1]))
                          .Append(i == 0 ? " m " : " l ");
                    sb.Append("h f Q\n");
                }
            }
            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        private static string FontResource(PageFont font)
        {
            switch (font)
            {
                case PageFont.HelveticaBold: return "F2";
                case PageFont.HelveticaOblique: return "F3";
                case PageFont.Courier: return "F4";
                default: return "F1";
            }
        }

        private static string Paint(bool stroke, bool fill)
        {
            return stroke && fill ? "B" : fill ? "f" : "S";
        }

        private static string Dash(double[] dash)
        {
            if (dash == null || dash.Length == 0) return "[] 0 d";
            var parts = new List<string>();
            foreach (var d in dash) parts.Add(F(d));
            return "[" + string.Join(" ", parts.ToArray()) + "] 0 d";
        }

        /// <summary>A circle as four cubic Bezier quarter arcs.</summary>
        private static void Circle(StringBuilder sb, double cx, double cy, double r)
        {
            const double k = 0.5522847498;
            var c = r * k;
            sb.Append(F(cx + r)).Append(' ').Append(F(cy)).Append(" m ");
            sb.Append(F(cx + r)).Append(' ').Append(F(cy + c)).Append(' ').Append(F(cx + c)).Append(' ').Append(F(cy + r)).Append(' ').Append(F(cx)).Append(' ').Append(F(cy + r)).Append(" c ");
            sb.Append(F(cx - c)).Append(' ').Append(F(cy + r)).Append(' ').Append(F(cx - r)).Append(' ').Append(F(cy + c)).Append(' ').Append(F(cx - r)).Append(' ').Append(F(cy)).Append(" c ");
            sb.Append(F(cx - r)).Append(' ').Append(F(cy - c)).Append(' ').Append(F(cx - c)).Append(' ').Append(F(cy - r)).Append(' ').Append(F(cx)).Append(' ').Append(F(cy - r)).Append(" c ");
            sb.Append(F(cx + c)).Append(' ').Append(F(cy - r)).Append(' ').Append(F(cx + r)).Append(' ').Append(F(cy - c)).Append(' ').Append(F(cx + r)).Append(' ').Append(F(cy)).Append(" c h ");
        }

        private static string F(double value) { return PageNumbers.Format(value); }

        /// <summary>zlib (RFC 1950): a two-byte header, raw DEFLATE data, and the Adler-32 checksum, big-endian.</summary>
        internal static byte[] Zlib(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                output.WriteByte(0x78);
                output.WriteByte(0x9C);
                using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
                    deflate.Write(data, 0, data.Length);
                uint a = 1, b = 0;
                foreach (var d in data)
                {
                    a = (a + d) % 65521;
                    b = (b + a) % 65521;
                }
                var adler = (b << 16) | a;
                output.WriteByte((byte)(adler >> 24));
                output.WriteByte((byte)(adler >> 16));
                output.WriteByte((byte)(adler >> 8));
                output.WriteByte((byte)adler);
                return output.ToArray();
            }
        }
    }
}
