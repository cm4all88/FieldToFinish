using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace CrewUpload.App
{
    /// <summary>The company look, from the branding section of job-folders.json.</summary>
    internal static class Theme
    {
        public static Color Primary = Color.FromArgb(0, 57, 93);
        public static Color Highlight = Color.FromArgb(120, 190, 32);
        public static readonly Color Muted = Color.FromArgb(96, 96, 96);
        public static readonly Color Tile = Color.FromArgb(244, 247, 250);
        public static string CompanyName = "Parametrix";
        public static string AppTitle = "Crew Upload";
        public static Image Logo;

        public static void Apply(JobFolderConfig config)
        {
            var b = config.Branding ?? new Branding();
            Primary = Parse(b.PrimaryColor, Primary);
            Highlight = Parse(b.AccentColor, Highlight);
            CompanyName = string.IsNullOrWhiteSpace(b.CompanyName) ? string.Empty : b.CompanyName.Trim();
            AppTitle = string.IsNullOrWhiteSpace(b.AppTitle) ? "Crew Upload" : b.AppTitle.Trim();
            var logo = config.ResolveFile(b.Logo);
            if (logo != null)
            {
                try
                {
                    // Copied out of the file so the PNG on the share is not held open.
                    using (var img = Image.FromFile(logo)) Logo = new Bitmap(img);
                }
                catch (Exception e) when (e is OutOfMemoryException || e is IOException || e is ArgumentException)
                {
                    Logo = null; // not an image: fall back to the wordmark
                }
            }
        }

        private static Color Parse(string hex, Color fallback)
        {
            int rgb;
            return hex != null && hex.Length == 7 && int.TryParse(hex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb)
                ? Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255)
                : fallback;
        }

        /// <summary>A lighter version of a colour, for hover and drag-over fills.</summary>
        public static Color Tint(Color c, double amount) =>
            Color.FromArgb((int)(c.R + (255 - c.R) * amount), (int)(c.G + (255 - c.G) * amount), (int)(c.B + (255 - c.B) * amount));

        /// <summary>The header band: logo (or company wordmark) on the left, app title beside it.</summary>
        public static Control Header()
        {
            var band = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Primary };
            band.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                float x = 18;
                if (Logo != null)
                {
                    var h = band.Height - 20f;
                    var w = Logo.Width * h / Logo.Height;
                    g.DrawImage(Logo, x, 10, w, h);
                    x += w + 18;
                }
                else if (CompanyName.Length > 0)
                {
                    using (var f = new Font("Segoe UI", 18f, FontStyle.Bold))
                    using (var ink = new SolidBrush(Color.White))
                    {
                        var text = CompanyName.ToUpperInvariant();
                        var size = g.MeasureString(text, f);
                        g.DrawString(text, f, ink, x, (band.Height - size.Height) / 2);
                        x += size.Width + 10;
                    }
                }
                using (var bar = new SolidBrush(Highlight)) g.FillRectangle(bar, x, 16, 3, band.Height - 32);
                using (var f = new Font("Segoe UI Light", 16f))
                using (var ink = new SolidBrush(Color.White))
                {
                    var size = g.MeasureString(AppTitle, f);
                    g.DrawString(AppTitle, f, ink, x + 12, (band.Height - size.Height) / 2);
                }
                using (var line = new SolidBrush(Highlight)) g.FillRectangle(line, 0, band.Height - 4, band.Width, 4);
            };
            band.Resize += (s, e) => band.Invalidate();
            return band;
        }
    }

    /// <summary>
    /// One drop box per kind of upload. Whatever lands on it is that type -- nothing is guessed.
    /// Clicking it opens a file picker for that type.
    /// </summary>
    internal sealed class DropBox : Panel
    {
        private readonly Label _count;
        private bool _hot;

        public UploadCategory Category { get; }

        public event Action<DropBox, string[]> FilesDropped;
        public event Action<DropBox> Browse;

        public DropBox(UploadCategory category)
        {
            Category = category;
            DoubleBuffered = true;
            AllowDrop = true;
            Cursor = Cursors.Hand;
            BackColor = Theme.Tile;
            Margin = new Padding(5);
            Dock = DockStyle.Fill;
            MinimumSize = new Size(120, 112);

            var name = new Label
            {
                Text = category.Name, Dock = DockStyle.Top, Height = 46, TextAlign = ContentAlignment.BottomCenter,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = Theme.Primary, BackColor = Color.Transparent,
            };
            var hint = new Label
            {
                Text = "drop here", Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.TopCenter,
                ForeColor = Theme.Muted, BackColor = Color.Transparent,
            };
            _count = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopCenter, Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Theme.Highlight, BackColor = Color.Transparent,
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
                c.MouseEnter += (s, e) => { if (!_hot) BackColor = Theme.Tint(Theme.Primary, 0.9); };
                c.MouseLeave += (s, e) => { if (!_hot && !ClientRectangle.Contains(PointToClient(MousePosition))) BackColor = Theme.Tile; };
            }
            SetCount(0);
        }

        public void SetCount(int n) => _count.Text = n == 0 ? string.Empty : n + " file" + (n == 1 ? "" : "s") + " queued";

        private void Hot(bool on)
        {
            _hot = on;
            BackColor = on ? Theme.Tint(Theme.Highlight, 0.75) : Theme.Tile;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(_hot ? Theme.Highlight : Theme.Tint(Theme.Primary, 0.45), _hot ? 3 : 2) { DashStyle = DashStyle.Dash })
                e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Invalidate();
        }
    }
}
