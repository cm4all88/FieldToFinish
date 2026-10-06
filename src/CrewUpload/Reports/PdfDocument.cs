using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CrewUpload.Reports
{
    /// <summary>
    /// A very small PDF writer: Letter pages, Helvetica and Helvetica-Bold (built into every PDF
    /// reader, so nothing is embedded), text, lines, boxes. Enough for a form, with no library to
    /// install on the crews' PCs. Coordinates are points from the top-left corner.
    /// </summary>
    internal sealed class PdfDocument
    {
        public const float Width = 612f, Height = 792f;

        private readonly List<StringBuilder> _pages = new List<StringBuilder>();
        private StringBuilder _page;

        public string Title { get; set; }

        public int PageCount => _pages.Count;

        public void NewPage()
        {
            _page = new StringBuilder();
            _pages.Add(_page);
        }

        public void Text(float x, float y, string text, float size, bool bold = false, float gray = 0.2f)
        {
            if (string.IsNullOrEmpty(text)) return;
            Op(F(gray) + " g BT /" + (bold ? "F2" : "F1") + " " + F(size) + " Tf " + F(x) + " " + F(Height - y) + " Td (" + Escape(text) + ") Tj ET");
        }

        public void TextRight(float right, float y, string text, float size, bool bold = false) =>
            Text(right - Measure(text, size, bold), y, text, size, bold);

        public void TextCentered(float center, float y, string text, float size, bool bold = false) =>
            Text(center - Measure(text, size, bold) / 2, y, text, size, bold);

        public void Line(float x1, float y1, float x2, float y2, float width = 0.6f, float gray = 0.2f) =>
            Op(F(gray) + " G " + F(width) + " w " + F(x1) + " " + F(Height - y1) + " m " + F(x2) + " " + F(Height - y2) + " l S");

        public void Box(float x, float y, float w, float h, float width = 0.6f, float gray = 0.2f) =>
            Op(F(gray) + " G " + F(width) + " w " + F(x) + " " + F(Height - y - h) + " " + F(w) + " " + F(h) + " re S");

        public void Fill(float x, float y, float w, float h, float gray) =>
            Op(F(gray) + " g " + F(x) + " " + F(Height - y - h) + " " + F(w) + " " + F(h) + " re f");

        /// <summary>A form checkbox; checked ones are filled, as on the paper form.</summary>
        public void CheckBox(float x, float y, bool on, float size = 8f)
        {
            if (on) Fill(x, y, size, size, 0.15f);
            else Box(x, y, size, size, 0.6f, 0.25f);
        }

        /// <summary>Splits text into lines no wider than <paramref name="width"/>, keeping the crew's own line breaks.</summary>
        public static List<string> Wrap(string text, float width, float size, bool bold = false)
        {
            var lines = new List<string>();
            foreach (var para in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                var line = string.Empty;
                foreach (var word in para.Split(' '))
                {
                    var candidate = line.Length == 0 ? word : line + " " + word;
                    if (Measure(candidate, size, bold) <= width) { line = candidate; continue; }
                    if (line.Length > 0) lines.Add(line);
                    line = word;
                    while (Measure(line, size, bold) > width && line.Length > 1)
                    {
                        var n = line.Length - 1;
                        while (n > 1 && Measure(line.Substring(0, n), size, bold) > width) n--;
                        lines.Add(line.Substring(0, n));
                        line = line.Substring(n);
                    }
                }
                lines.Add(line);
            }
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            return lines;
        }

        public static float Measure(string text, float size, bool bold = false)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var widths = bold ? HelveticaBold : Helvetica;
            var total = 0;
            foreach (var b in WinAnsi(text))
                total += b >= 32 && b <= 126 ? widths[b - 32] : 556;
            return total * size / 1000f;
        }

        public byte[] ToBytes()
        {
            if (_pages.Count == 0) NewPage();
            var objects = new List<byte[]>();
            // 1 catalog, 2 pages, 3 Helvetica, 4 Helvetica-Bold, 5 info, then page + content pairs
            var kids = new StringBuilder();
            for (var i = 0; i < _pages.Count; i++) kids.Append(6 + i * 2).Append(" 0 R ");
            objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
            objects.Add(Ascii("<< /Type /Pages /Kids [" + kids + "] /Count " + _pages.Count + " >>"));
            objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
            objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));
            var info = new List<byte>(Ascii("<< /Producer (Crew Upload) /CreationDate (D:" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ")"));
            if (!string.IsNullOrEmpty(Title)) { info.AddRange(Ascii(" /Title (")); info.AddRange(WinAnsi(EscapeRaw(Title))); info.AddRange(Ascii(")")); }
            info.AddRange(Ascii(" >>"));
            objects.Add(info.ToArray());
            for (var i = 0; i < _pages.Count; i++)
            {
                objects.Add(Ascii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents " + (7 + i * 2) + " 0 R >>"));
                var content = WinAnsi(_pages[i].ToString());
                var stream = new List<byte>(Ascii("<< /Length " + content.Length + " >>\nstream\n"));
                stream.AddRange(content);
                stream.AddRange(Ascii("\nendstream"));
                objects.Add(stream.ToArray());
            }

            using (var ms = new MemoryStream())
            {
                Write(ms, Ascii("%PDF-1.4\n"));
                Write(ms, new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });
                var offsets = new List<long>();
                for (var i = 0; i < objects.Count; i++)
                {
                    offsets.Add(ms.Position);
                    Write(ms, Ascii((i + 1) + " 0 obj\n"));
                    Write(ms, objects[i]);
                    Write(ms, Ascii("\nendobj\n"));
                }
                var xref = ms.Position;
                var sb = new StringBuilder("xref\n0 " + (objects.Count + 1) + "\n0000000000 65535 f \n");
                foreach (var o in offsets) sb.Append(o.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
                sb.Append("trailer\n<< /Size " + (objects.Count + 1) + " /Root 1 0 R /Info 5 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
                Write(ms, Ascii(sb.ToString()));
                return ms.ToArray();
            }
        }

        private void Op(string op)
        {
            if (_page == null) NewPage();
            _page.Append(op).Append('\n');
        }

        private static void Write(Stream s, byte[] b) => s.Write(b, 0, b.Length);

        private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

        private static string F(float v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>Escapes a PDF string; characters outside WinAnsi become '?'.</summary>
        private static string Escape(string s) => EscapeRaw(s);

        private static string EscapeRaw(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (var c in s)
            {
                if (c == '\\' || c == '(' || c == ')') sb.Append('\\').Append(c);
                else if (c == '\t') sb.Append(' ');
                else if (c < 32) continue;
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>The text as WinAnsiEncoding bytes (what the standard fonts use).</summary>
        internal static byte[] WinAnsi(string s)
        {
            var bytes = new byte[s.Length];
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                int b;
                if (c < 128 || (c >= 0xA0 && c <= 0xFF)) b = c;
                else if (!Cp1252.TryGetValue(c, out b)) b = '?';
                bytes[i] = (byte)b;
            }
            return bytes;
        }

        private static readonly Dictionary<char, int> Cp1252 = new Dictionary<char, int>
        {
            ['€'] = 0x80, ['‚'] = 0x82, ['ƒ'] = 0x83, ['„'] = 0x84, ['…'] = 0x85, ['†'] = 0x86, ['‡'] = 0x87,
            ['ˆ'] = 0x88, ['‰'] = 0x89, ['Š'] = 0x8A, ['‹'] = 0x8B, ['Œ'] = 0x8C, ['Ž'] = 0x8E,
            ['‘'] = 0x91, ['’'] = 0x92, ['“'] = 0x93, ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97,
            ['˜'] = 0x98, ['™'] = 0x99, ['š'] = 0x9A, ['›'] = 0x9B, ['œ'] = 0x9C, ['ž'] = 0x9E, ['Ÿ'] = 0x9F,
        };

        // Advance widths (1/1000 em) of the standard fonts, characters 32-126, from Adobe's AFM files.
        private static readonly int[] Helvetica =
        {
            278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
            1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
            333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
            556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
        };

        private static readonly int[] HelveticaBold =
        {
            278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
            975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
            333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
            611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
        };
    }
}
