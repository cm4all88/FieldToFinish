using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CrewUpload.App
{
    /// <summary>
    /// The Parametrix look, per the brand guide: charcoal, red and white carry the window;
    /// red is used sparingly (the Upload button, problems, the spacer arrow); the secondary
    /// palette only tells the upload types apart. Values come from the branding section of
    /// job-folders.json.
    /// </summary>
    internal static class Theme
    {
        public static Color Charcoal = Hex("#333333");
        public static Color Red = Hex("#EE3D24");
        public static readonly Color MediumGray = Hex("#676768");
        public static readonly Color LightGray1 = Hex("#B3B4B5");
        public static readonly Color LightGray2 = Hex("#DBDDDC");
        public static readonly Color LightGray3 = Hex("#E5E5E5");
        public static readonly Color LightGray4 = Hex("#F2F2F2");

        public static Color Muted => MediumGray;

        public static string CompanyName = "Parametrix";
        public static string AppTitle = "Crew Upload";
        public static Image Logo;
        public static Image Ix;
        public static string HeadlineFamily = "Georgia";
        public static string BodyFamily = "Segoe UI";

        public static void Apply(JobFolderConfig config)
        {
            var b = config.Branding ?? new Branding();
            Charcoal = Parse(b.PrimaryColor, Charcoal);
            Red = Parse(b.AccentColor, Red);
            CompanyName = (b.CompanyName ?? string.Empty).Trim();
            AppTitle = string.IsNullOrWhiteSpace(b.AppTitle) ? "Crew Upload" : b.AppTitle.Trim();
            Logo = LoadImage(config.ResolveFile(b.Logo));
            Ix = LoadImage(config.ResolveFile(b.IxMark));

            var installed = new HashSet<string>(new InstalledFontCollection().Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            HeadlineFamily = b.HeadlineFonts.FirstOrDefault(installed.Contains) ?? HeadlineFamily;
            BodyFamily = b.BodyFonts.FirstOrDefault(installed.Contains) ?? BodyFamily;
        }

        private static Image LoadImage(string path)
        {
            if (path == null) return null;
            try
            {
                // Copied out of the file so the PNG on the share is not held open.
                using (var img = Image.FromFile(path)) return new Bitmap(img);
            }
            catch (Exception e) when (e is OutOfMemoryException || e is IOException || e is ArgumentException)
            {
                return null;
            }
        }

        public static Font Body(float size = 10f, FontStyle style = FontStyle.Regular)
        {
            // Franklin Gothic ships as separate Book/Medium/Demi families; bold is Demi.
            if ((style & FontStyle.Bold) != 0 && BodyFamily.StartsWith("Franklin Gothic Book", StringComparison.OrdinalIgnoreCase))
                return new Font("Franklin Gothic Demi", size, style & ~FontStyle.Bold);
            return new Font(BodyFamily, size, style);
        }

        public static Font Headline(float size) => new Font(HeadlineFamily, size);

        public static Color Hex(string hex) => Parse(hex, Color.Gray);

        public static Color Parse(string hex, Color fallback)
        {
            int rgb;
            return hex != null && hex.Length == 7 && hex[0] == '#' && int.TryParse(hex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb)
                ? Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255)
                : fallback;
        }

        /// <summary>A lighter version of a colour, for drag-over fills.</summary>
        public static Color Tint(Color c, double amount) =>
            Color.FromArgb((int)(c.R + (255 - c.R) * amount), (int)(c.G + (255 - c.G) * amount), (int)(c.B + (255 - c.B) * amount));

        /// <summary>The guide's spacer arrow: a 1pt line with a barbed head.</summary>
        public static void SpacerArrow(Graphics g, float x, float y, float length, Color color)
        {
            using (var pen = new Pen(color, 1.3f))
                g.DrawLine(pen, x, y, x + length - 6, y);
            using (var head = new SolidBrush(color))
                g.FillPolygon(head, new[] { new PointF(x + length, y), new PointF(x + length - 9, y - 3.2f), new PointF(x + length - 6.5f, y), new PointF(x + length - 9, y + 3.2f) });
        }

        /// <summary>
        /// White header: the primary logo with its clear space, a rule, the app title in the
        /// headline face and the red spacer arrow under it. No tagline -- the guide keeps it off
        /// headers.
        /// </summary>
        public static Control Header()
        {
            var band = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Color.White };
            band.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                float x = 26;
                if (Logo != null)
                {
                    const float h = 34f;
                    var w = Logo.Width * h / Logo.Height;
                    g.DrawImage(Logo, x, (band.Height - h) / 2, w, h);
                    x += w + 26;
                }
                else if (CompanyName.Length > 0)
                {
                    // No logo file: the name in plain text, never an imitation of the mark.
                    using (var f = Body(18f, FontStyle.Bold))
                    using (var ink = new SolidBrush(Charcoal))
                    {
                        var size = g.MeasureString(CompanyName, f);
                        g.DrawString(CompanyName, f, ink, x, (band.Height - size.Height) / 2);
                        x += size.Width + 22;
                    }
                }
                using (var rule = new Pen(LightGray2, 1)) g.DrawLine(rule, x, 22, x, band.Height - 22);
                x += 22;
                using (var f = Headline(19f))
                using (var ink = new SolidBrush(Charcoal))
                {
                    var size = g.MeasureString(AppTitle, f);
                    var top = (band.Height - size.Height) / 2 - 5;
                    g.DrawString(AppTitle, f, ink, x, top);
                    SpacerArrow(g, x + 3, top + size.Height + 4, 44, Red);
                }
                using (var line = new Pen(LightGray2, 1)) g.DrawLine(line, 0, band.Height - 1, band.Width, band.Height - 1);
            };
            band.Resize += (s, e) => band.Invalidate();
            return band;
        }

        /// <summary>Charcoal outline buttons; the one primary action is solid Parametrix Red.</summary>
        public static Button Button(string text, bool primary)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Padding = new Padding(12, 4, 12, 4), Margin = new Padding(3, 3, 8, 3),
                BackColor = primary ? Red : Color.White, ForeColor = primary ? Color.White : Charcoal, Cursor = Cursors.Hand,
                Font = primary ? Body(11.5f, FontStyle.Bold) : Body(10f),
            };
            b.FlatAppearance.BorderColor = primary ? Red : Charcoal;
            b.FlatAppearance.MouseOverBackColor = primary ? Tint(Red, 0.12) : LightGray4;
            b.EnabledChanged += (s, e) =>
            {
                if (!primary) return;
                b.BackColor = b.Enabled ? Red : LightGray2;
                b.FlatAppearance.BorderColor = b.Enabled ? Red : LightGray2;
            };
            return b;
        }
    }

    /// <summary>
    /// One drop box per kind of upload. Whatever lands on it is that type -- nothing is guessed.
    /// Clicking it opens a file picker for that type. Its band is the type's colour, which the
    /// Type column repeats so a file's box is easy to see in the list.
    /// </summary>
    internal sealed class DropBox : Panel
    {
        private const int Band = 7;
        private readonly Label _count;
        private readonly Color _color;
        private bool _hot;
        private bool _hover;

        public UploadCategory Category { get; }

        public event Action<DropBox, string[]> FilesDropped;
        public event Action<DropBox> Browse;

        public DropBox(UploadCategory category)
        {
            Category = category;
            _color = Theme.Parse(category.Color, Theme.MediumGray);
            DoubleBuffered = true;
            AllowDrop = true;
            Cursor = Cursors.Hand;
            BackColor = Color.White;
            Margin = new Padding(6);
            Padding = new Padding(4, Band + 4, 4, 4);
            Dock = DockStyle.Fill;
            MinimumSize = new Size(120, 112);

            var name = new Label
            {
                Text = category.Name, Dock = DockStyle.Top, Height = 48, TextAlign = ContentAlignment.BottomCenter,
                Font = Theme.Body(12f, FontStyle.Bold), ForeColor = Theme.Charcoal, BackColor = Color.Transparent,
            };
            var hint = new Label
            {
                Text = "drop here", Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.TopCenter,
                Font = Theme.Body(9.5f), ForeColor = Theme.MediumGray, BackColor = Color.Transparent,
            };
            _count = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopCenter, Font = Theme.Body(10f, FontStyle.Bold),
                ForeColor = Theme.Charcoal, BackColor = Color.Transparent,
            };
            Controls.Add(_count);
            Controls.Add(hint);
            Controls.Add(name);

            foreach (var c in new Control[] { this, name, hint, _count })
            {
                c.AllowDrop = true;
                c.DragEnter += (s, e) =>
                {
                    var ok = Enabled && e.Data.GetDataPresent(DataFormats.FileDrop);
                    e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
                    Hot(ok);
                };
                c.DragLeave += (s, e) => Hot(false);
                c.DragDrop += (s, e) =>
                {
                    Hot(false);
                    var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                    if (paths != null) FilesDropped?.Invoke(this, paths);
                };
                c.Click += (s, e) => Browse?.Invoke(this);
                c.MouseEnter += (s, e) => Hover(true);
                c.MouseLeave += (s, e) => Hover(ClientRectangle.Contains(PointToClient(MousePosition)));
            }
            SetCount(0);
        }

        public void SetCount(int n) => _count.Text = n == 0 ? string.Empty : n + " file" + (n == 1 ? "" : "s") + " queued";

        private void Hot(bool on)
        {
            _hot = on;
            Restyle();
        }

        private void Hover(bool on)
        {
            _hover = on;
            Restyle();
        }

        private void Restyle()
        {
            BackColor = _hot ? Theme.Tint(_color, 0.82) : _hover ? Theme.LightGray4 : Color.White;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            using (var band = new SolidBrush(_color)) g.FillRectangle(band, 0, 0, Width, Band);
            if (_hot)
            {
                using (var pen = new Pen(_color, 2)) g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
            }
            else
            {
                using (var pen = new Pen(Theme.LightGray1, 1) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, 0, Band, Width - 1, Height - Band - 1);
            }
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Invalidate();
        }
    }
}
