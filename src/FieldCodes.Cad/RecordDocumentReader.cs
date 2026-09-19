using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using FieldCodes.RecordSurvey;
using FieldCodes.Settings;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Reads a recorded survey document into positioned text. Two readers:
    ///
    ///   Windows  -- the OCR engine and PDF renderer Windows ships with (Windows.Media.Ocr,
    ///               Windows.Data.Pdf), reached through the Windows 10 SDK contracts. No
    ///               third-party install, no service, nothing leaves the machine. Survey text
    ///               runs at every angle, so each page is read at several rotations and the
    ///               hits are mapped back onto the page and merged. Large sheets are read in
    ///               overlapping tiles because the engine caps the image size.
    ///   Sidecar  -- a .ocr.json beside the document, written by any other OCR tool in the
    ///               DocumentText schema (or by a previous FTFRECORD run). Also what the
    ///               headless smoke test feeds in.
    ///
    /// Windows OCR reports no per-word confidence. The confidence recorded here is honest
    /// about that: a line read once is 0.90; the same text read again at another rotation or
    /// in an overlapping tile is 0.97; a spot read differently twice keeps both readings and
    /// is flagged by the extraction. The parser then lowers it further for every repaired
    /// character. Nothing is ever raised by guesswork.
    ///
    /// UNTESTED against a document inside Civil 3D. The rotation mapping and merge are unit
    /// tested in FieldCodes (PageGeometry); this file only feeds them.
    /// </summary>
    internal static class RecordDocumentReader
    {
        public const double SingleReadConfidence = 0.90;
        public const double AgreedReadConfidence = 0.97;

        public static readonly string[] ImageExtensions = { ".pdf", ".tif", ".tiff", ".jpg", ".jpeg", ".png", ".bmp" };

        /// <summary>Where page images and OCR files are kept: never beside the recorded document.</summary>
        public static string CacheFolder(string documentPath)
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldToFinish", "record-pages");
            var name = Path.GetFileNameWithoutExtension(documentPath ?? "document");
            var safe = new string(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
            var stamp = string.Empty;
            try { stamp = File.GetLastWriteTimeUtc(documentPath).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture); } catch (Exception) { }
            return Path.Combine(root, safe + "-" + stamp);
        }

        /// <summary>The sidecar OCR file for a document.</summary>
        public static string SidecarPath(string documentPath)
        {
            if (documentPath.EndsWith(".ocr.json", StringComparison.OrdinalIgnoreCase)) return documentPath;
            return Path.Combine(Path.GetDirectoryName(documentPath) ?? string.Empty, Path.GetFileNameWithoutExtension(documentPath) + ".ocr.json");
        }

        /// <summary>
        /// Reads the document with the configured engine. A .ocr.json path reads the sidecar
        /// regardless of the setting. Progress lines go to <paramref name="say"/>.
        /// </summary>
        public static DocumentText Read(string path, RecordSurveySettings settings, Action<string> say)
        {
            say = say ?? delegate { };
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new ConfigException("The document \"" + path + "\" does not exist.");

            if (path.EndsWith(".ocr.json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(settings.OcrEngine, RecordSurveySettings.OcrSidecar, StringComparison.OrdinalIgnoreCase))
            {
                var sidecar = SidecarPath(path);
                if (!File.Exists(sidecar))
                    throw new ConfigException("The Sidecar OCR engine is selected but \"" + sidecar + "\" does not exist. Write the document's text there in the FTF OCR schema, or choose the Windows engine under Settings > Recorded Surveys.");
                say("Reading OCR text from " + sidecar);
                var text = DocumentText.FromJson(File.ReadAllText(sidecar));
                if (string.IsNullOrEmpty(text.DocumentPath)) text.DocumentPath = path;
                // Page images named relative to the sidecar are resolved against it.
                foreach (var page in text.Pages)
                    if (!string.IsNullOrEmpty(page.ImagePath) && !Path.IsPathRooted(page.ImagePath))
                        page.ImagePath = Path.Combine(Path.GetDirectoryName(sidecar) ?? string.Empty, page.ImagePath);
                return text;
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (Array.IndexOf(ImageExtensions, ext) < 0)
                throw new ConfigException("FTFRECORD reads PDF, TIFF, JPG and PNG documents (or a .ocr.json sidecar); \"" + ext + "\" is not one of them.");

            var result = new DocumentText { DocumentPath = path, Reader = "Windows OCR " + settings.OcrDpi + " dpi, rotations " + settings.OcrRotations };
            var cache = CacheFolder(path);
            if (settings.KeepPageImages) Directory.CreateDirectory(cache);

            var pages = RenderPages(path, settings.OcrDpi, say);
            var number = 0;
            foreach (var bitmap in pages)
            {
                number++;
                using (bitmap)
                {
                    var page = new DocumentPage { Number = number, WidthPx = bitmap.Width, HeightPx = bitmap.Height, Dpi = settings.OcrDpi };
                    if (settings.KeepPageImages)
                    {
                        page.ImagePath = Path.Combine(cache, "page-" + number.ToString("000", CultureInfo.InvariantCulture) + ".png");
                        try { bitmap.Save(page.ImagePath, ImageFormat.Png); }
                        catch (Exception ex) { result.Notes.Add("Page " + number + ": the page image could not be saved for the review (" + ex.Message + ")."); page.ImagePath = null; }
                    }
                    say(string.Format(CultureInfo.InvariantCulture, "Page {0}: {1} x {2} px", number, bitmap.Width, bitmap.Height));
                    var hits = new List<DocumentLine>();
                    foreach (var rotation in settings.RotationList())
                    {
                        var before = hits.Count;
                        try { hits.AddRange(ReadRotated(bitmap, rotation, settings.OcrLanguage)); }
                        catch (Exception ex)
                        {
                            result.Notes.Add("Page " + number + " at " + rotation + "°: OCR failed (" + ex.Message + ").");
                            say("  rotation " + rotation + "°: failed -- " + ex.Message);
                            continue;
                        }
                        say(string.Format(CultureInfo.InvariantCulture, "  rotation {0}°: {1} line(s)", rotation, hits.Count - before));
                    }
                    Agree(hits);
                    page.Lines = PageGeometry.Merge(hits);
                    say(string.Format(CultureInfo.InvariantCulture, "  {0} distinct line(s) after merging the passes", page.Lines.Count));
                    result.Pages.Add(page);
                }
            }
            if (result.Pages.Count == 0) result.Notes.Add("No page could be rendered.");
            return result;
        }

        /// <summary>Text found twice at the same spot, reading the same, is trusted more than a single read.</summary>
        private static void Agree(List<DocumentLine> hits)
        {
            for (var i = 0; i < hits.Count; i++)
            {
                var a = hits[i];
                var agreed = false;
                for (var j = 0; j < hits.Count && !agreed; j++)
                {
                    if (i == j) continue;
                    var b = hits[j];
                    if (PageGeometry.IsSameSpot(a.Box, b.Box, 0.6) && string.Equals(Canon(a.Text), Canon(b.Text), StringComparison.Ordinal)) agreed = true;
                }
                a.Confidence = agreed ? AgreedReadConfidence : SingleReadConfidence;
                foreach (var w in a.Words) w.Confidence = a.Confidence;
            }
        }

        private static string Canon(string s)
        {
            return new string((s ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        }

        // ------------------------------------------------------------ rendering

        /// <summary>Every page of the document as a bitmap at the requested dpi.</summary>
        private static IEnumerable<Bitmap> RenderPages(string path, int dpi, Action<string> say)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".pdf") return RenderPdf(path, dpi, say);
            return LoadImagePages(path, say);
        }

        private static IEnumerable<Bitmap> LoadImagePages(string path, Action<string> say)
        {
            var pages = new List<Bitmap>();
            using (var image = Image.FromFile(path))
            {
                var dimension = new FrameDimension(image.FrameDimensionsList[0]);
                var count = Math.Max(1, image.GetFrameCount(dimension));
                for (var i = 0; i < count; i++)
                {
                    image.SelectActiveFrame(dimension, i);
                    var copy = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
                    copy.SetResolution(image.HorizontalResolution > 0 ? image.HorizontalResolution : 300f, image.VerticalResolution > 0 ? image.VerticalResolution : 300f);
                    using (var g = Graphics.FromImage(copy))
                    {
                        g.Clear(Color.White);
                        g.DrawImage(image, 0, 0, image.Width, image.Height);
                    }
                    pages.Add(copy);
                }
            }
            say(pages.Count + " page(s) in the image file");
            return pages;
        }

        private static IEnumerable<Bitmap> RenderPdf(string path, int dpi, Action<string> say)
        {
            var pages = new List<Bitmap>();
            var file = Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask().GetAwaiter().GetResult();
            var document = Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file).AsTask().GetAwaiter().GetResult();
            say(document.PageCount + " page(s) in the PDF");
            for (uint i = 0; i < document.PageCount; i++)
            {
                using (var page = document.GetPage(i))
                {
                    // PdfPage.Size is in device-independent pixels (1/96 inch).
                    var widthPx = (uint)Math.Max(1, Math.Round(page.Size.Width / 96.0 * dpi));
                    var heightPx = (uint)Math.Max(1, Math.Round(page.Size.Height / 96.0 * dpi));
                    var options = new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = widthPx, DestinationHeight = heightPx };
                    using (var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                    {
                        page.RenderToStreamAsync(stream, options).AsTask().GetAwaiter().GetResult();
                        var bytes = new byte[stream.Size];
                        using (var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0)))
                        {
                            reader.LoadAsync((uint)stream.Size).AsTask().GetAwaiter().GetResult();
                            reader.ReadBytes(bytes);
                        }
                        using (var ms = new MemoryStream(bytes))
                        using (var decoded = Image.FromStream(ms))
                        {
                            var copy = new Bitmap(decoded.Width, decoded.Height, PixelFormat.Format24bppRgb);
                            copy.SetResolution(dpi, dpi);
                            using (var g = Graphics.FromImage(copy))
                            {
                                g.Clear(Color.White);
                                g.DrawImage(decoded, 0, 0, decoded.Width, decoded.Height);
                            }
                            pages.Add(copy);
                        }
                    }
                }
            }
            return pages;
        }

        // ------------------------------------------------------------ OCR passes

        /// <summary>The page turned clockwise by the pass angle, read in tiles, every hit mapped back onto the page.</summary>
        private static List<DocumentLine> ReadRotated(Bitmap page, double rotationDegrees, string language)
        {
            var hits = new List<DocumentLine>();
            double cw, ch;
            PageGeometry.RotatedCanvas(page.Width, page.Height, rotationDegrees, out cw, out ch);
            Bitmap canvas = null;
            try
            {
                canvas = Math.Abs(rotationDegrees) < 1e-9 ? page : Rotate(page, rotationDegrees, (int)cw, (int)ch);
                foreach (var tile in Tiles(canvas.Width, canvas.Height, MaxDimension()))
                {
                    using (var piece = canvas.Clone(tile, PixelFormat.Format24bppRgb))
                    {
                        foreach (var line in Recognize(piece, language))
                        {
                            var box = new PageBox(line.Box.X + tile.X, line.Box.Y + tile.Y, line.Box.Width, line.Box.Height);
                            var mapped = PageGeometry.Unrotate(box, rotationDegrees, canvas.Width, canvas.Height, page.Width, page.Height);
                            var hit = new DocumentLine(line.Text, mapped, SingleReadConfidence) { PassRotationDegrees = rotationDegrees };
                            foreach (var w in line.Words)
                            {
                                var wb = new PageBox(w.Box.X + tile.X, w.Box.Y + tile.Y, w.Box.Width, w.Box.Height);
                                hit.Words.Add(new DocumentWord(w.Text, PageGeometry.Unrotate(wb, rotationDegrees, canvas.Width, canvas.Height, page.Width, page.Height), SingleReadConfidence));
                            }
                            hits.Add(hit);
                        }
                    }
                }
            }
            finally
            {
                if (canvas != null && !ReferenceEquals(canvas, page)) canvas.Dispose();
            }
            return hits;
        }

        private static Bitmap Rotate(Bitmap source, double degreesClockwise, int canvasWidth, int canvasHeight)
        {
            var dst = new Bitmap(Math.Max(1, canvasWidth), Math.Max(1, canvasHeight), PixelFormat.Format24bppRgb);
            dst.SetResolution(source.HorizontalResolution, source.VerticalResolution);
            using (var g = Graphics.FromImage(dst))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TranslateTransform(canvasWidth / 2f, canvasHeight / 2f);
                g.RotateTransform((float)degreesClockwise);          // GDI+ turns clockwise on screen for a positive angle
                g.TranslateTransform(-source.Width / 2f, -source.Height / 2f);
                g.DrawImage(source, 0, 0, source.Width, source.Height);
            }
            return dst;
        }

        /// <summary>Overlapping tiles no larger than the engine's limit; one tile when the page fits.</summary>
        internal static IEnumerable<Rectangle> Tiles(int width, int height, int max)
        {
            if (width <= max && height <= max) { yield return new Rectangle(0, 0, width, height); yield break; }
            var step = (int)(max * 0.85);
            for (var y = 0; y < height; y += step)
            for (var x = 0; x < width; x += step)
            {
                var w = Math.Min(max, width - x);
                var h = Math.Min(max, height - y);
                yield return new Rectangle(x, y, w, h);
                if (x + max >= width) break;
            }
        }

        private static int MaxDimension()
        {
            try { return (int)Math.Min(4096, Windows.Media.Ocr.OcrEngine.MaxImageDimension); }
            catch (Exception) { return 2600; }
        }

        private static Windows.Media.Ocr.OcrEngine _engine;

        private static Windows.Media.Ocr.OcrEngine Engine(string language)
        {
            if (_engine != null) return _engine;
            Windows.Media.Ocr.OcrEngine engine = null;
            if (!string.IsNullOrWhiteSpace(language))
            {
                try { engine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(language)); }
                catch (Exception) { engine = null; }
            }
            if (engine == null) engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null)
                throw new ConfigException("Windows OCR is not available for language \"" + language + "\" on this machine. Install the language's OCR pack (Settings > Time & Language), or use the Sidecar engine.");
            _engine = engine;
            return engine;
        }

        private static List<DocumentLine> Recognize(Bitmap piece, string language)
        {
            var lines = new List<DocumentLine>();
            using (var ms = new MemoryStream())
            {
                piece.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                using (var ras = ms.AsRandomAccessStream())
                {
                    var decoder = Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras).AsTask().GetAwaiter().GetResult();
                    using (var software = decoder.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied).AsTask().GetAwaiter().GetResult())
                    {
                        var result = Engine(language).RecognizeAsync(software).AsTask().GetAwaiter().GetResult();
                        foreach (var line in result.Lines)
                        {
                            var words = line.Words.Select(w => new DocumentWord(w.Text, new PageBox(w.BoundingRect.X, w.BoundingRect.Y, w.BoundingRect.Width, w.BoundingRect.Height), SingleReadConfidence)).ToList();
                            if (words.Count == 0) continue;
                            PageBox box = null;
                            foreach (var w in words) box = box == null ? new PageBox(w.Box.X, w.Box.Y, w.Box.Width, w.Box.Height) : box.Union(w.Box);
                            var text = new DocumentLine(line.Text, box, SingleReadConfidence);
                            text.Words.AddRange(words);
                            lines.Add(text);
                        }
                    }
                }
            }
            return lines;
        }
    }
}
