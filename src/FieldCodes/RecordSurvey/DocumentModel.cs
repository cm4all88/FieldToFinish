using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace FieldCodes.RecordSurvey
{
    /// <summary>
    /// A rectangle on a document page, in page pixels with the origin at the top
    /// left (the convention every OCR engine and image viewer uses). Rotation is the
    /// angle of the text baseline in degrees, counter-clockwise, as it reads on the
    /// unrotated page: 0 for horizontal text, 90 for text running up the page.
    /// </summary>
    public sealed class PageBox
    {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("w")] public double Width { get; set; }
        [JsonProperty("h")] public double Height { get; set; }
        [JsonProperty("rot")] public double RotationDegrees { get; set; }

        public PageBox() { }

        public PageBox(double x, double y, double width, double height, double rotationDegrees = 0.0)
        {
            X = x; Y = y; Width = width; Height = height; RotationDegrees = rotationDegrees;
        }

        [JsonIgnore] public double CenterX { get { return X + Width / 2.0; } }
        [JsonIgnore] public double CenterY { get { return Y + Height / 2.0; } }
        [JsonIgnore] public double Right { get { return X + Width; } }
        [JsonIgnore] public double Bottom { get { return Y + Height; } }

        /// <summary>Area of overlap with another box, in square pixels (axis-aligned).</summary>
        public double Overlap(PageBox other)
        {
            if (other == null) return 0;
            var w = Math.Min(Right, other.Right) - Math.Max(X, other.X);
            var h = Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y);
            return w <= 0 || h <= 0 ? 0 : w * h;
        }

        [JsonIgnore] public double Area { get { return Math.Max(0, Width) * Math.Max(0, Height); } }

        /// <summary>The smallest box holding both.</summary>
        public PageBox Union(PageBox other)
        {
            if (other == null) return new PageBox(X, Y, Width, Height, RotationDegrees);
            var x0 = Math.Min(X, other.X);
            var y0 = Math.Min(Y, other.Y);
            return new PageBox(x0, y0, Math.Max(Right, other.Right) - x0, Math.Max(Bottom, other.Bottom) - y0, RotationDegrees);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0},{1:0}) {2:0}x{3:0}{4}", X, Y, Width, Height,
                Math.Abs(RotationDegrees) > 0.01 ? " @" + RotationDegrees.ToString("0.#", CultureInfo.InvariantCulture) + "°" : string.Empty);
        }
    }

    /// <summary>One recognised word: its text, where it sits, and the engine's confidence (0..1).</summary>
    public sealed class DocumentWord
    {
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("box")] public PageBox Box { get; set; }
        [JsonProperty("conf")] public double Confidence { get; set; }

        public DocumentWord() { Confidence = 1.0; }

        public DocumentWord(string text, PageBox box, double confidence)
        {
            Text = text; Box = box; Confidence = confidence;
        }
    }

    /// <summary>
    /// One recognised line of text -- the unit survey annotation is written in ("N 89°42'18" E 1320.45'").
    /// Word boxes are kept so a review can point at exactly the token a value came from.
    /// </summary>
    public sealed class DocumentLine
    {
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("box")] public PageBox Box { get; set; }
        [JsonProperty("conf")] public double Confidence { get; set; }
        [JsonProperty("words", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<DocumentWord> Words { get; set; }

        /// <summary>Which OCR pass produced this line (the page rotation it was read at), for diagnostics.</summary>
        [JsonProperty("pass")] public double PassRotationDegrees { get; set; }

        public DocumentLine() { Words = new List<DocumentWord>(); Confidence = 1.0; }

        public DocumentLine(string text, PageBox box, double confidence)
        {
            Text = text; Box = box; Confidence = confidence; Words = new List<DocumentWord>();
        }

        /// <summary>Confidence of the whole line: the engine's line value when it gives one, else the
        /// lowest word -- a line is only as trustworthy as its least certain token.</summary>
        [JsonIgnore]
        public double EffectiveConfidence
        {
            get
            {
                if (Words == null || Words.Count == 0) return Confidence;
                return Math.Min(Confidence, Words.Min(w => w.Confidence));
            }
        }
    }

    public sealed class DocumentPage
    {
        [JsonProperty("number")] public int Number { get; set; }
        [JsonProperty("widthPx")] public double WidthPx { get; set; }
        [JsonProperty("heightPx")] public double HeightPx { get; set; }
        /// <summary>Pixels per inch the page was read at; zero when unknown (a bare image).</summary>
        [JsonProperty("dpi")] public double Dpi { get; set; }
        [JsonProperty("lines", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<DocumentLine> Lines { get; set; }
        /// <summary>Path of the rendered page image, when the reader kept one for the review window.</summary>
        [JsonProperty("image")] public string ImagePath { get; set; }

        public DocumentPage() { Lines = new List<DocumentLine>(); }
    }

    /// <summary>
    /// Everything read from a recorded document: pages of positioned text. This is the
    /// only thing the extraction ever sees. The reader that produced it (Windows OCR, a
    /// text-layer PDF, a sidecar file written by another OCR tool) is interchangeable,
    /// which is what makes everything downstream testable without a document.
    /// </summary>
    public sealed class DocumentText
    {
        public const string Schema = "ftf-record-ocr-1";

        [JsonProperty("schema")] public string SchemaVersion { get; set; }
        [JsonProperty("document")] public string DocumentPath { get; set; }
        [JsonProperty("reader")] public string Reader { get; set; }
        [JsonProperty("readUtc")] public DateTime ReadUtc { get; set; }
        [JsonProperty("pages", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<DocumentPage> Pages { get; set; }
        /// <summary>Anything the reader wants the surveyor to know (a page it could not render, say).</summary>
        [JsonProperty("notes", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> Notes { get; set; }

        public DocumentText()
        {
            SchemaVersion = Schema;
            Pages = new List<DocumentPage>();
            Notes = new List<string>();
            ReadUtc = DateTime.UtcNow;
        }

        public string ToJson() { return JsonConvert.SerializeObject(this, Formatting.Indented); }

        public static DocumentText FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ConfigException("The OCR file is empty.");
            DocumentText text;
            try { text = JsonConvert.DeserializeObject<DocumentText>(json); }
            catch (JsonException ex) { throw new ConfigException("The OCR file is not valid JSON: " + ex.Message, ex); }
            if (text == null) throw new ConfigException("The OCR file is empty.");
            if (text.Pages == null) text.Pages = new List<DocumentPage>();
            foreach (var page in text.Pages)
            {
                if (page.Lines == null) page.Lines = new List<DocumentLine>();
                foreach (var line in page.Lines)
                {
                    if (line.Words == null) line.Words = new List<DocumentWord>();
                    if (line.Box == null) line.Box = new PageBox();
                }
            }
            if (text.Notes == null) text.Notes = new List<string>();
            return text;
        }
    }

    /// <summary>
    /// Where a value came from on the document: the page, the box of the text it was read
    /// from, and the raw text itself. Every extracted call carries one, so CAD geometry can
    /// be traced back to the recorded survey and the review can highlight the spot.
    /// </summary>
    public sealed class SourceRef
    {
        [JsonProperty("page")] public int Page { get; set; }
        [JsonProperty("box")] public PageBox Box { get; set; }
        [JsonProperty("text")] public string RawText { get; set; }
        /// <summary>The OCR engine's confidence in the text, 0..1.</summary>
        [JsonProperty("ocrConf")] public double OcrConfidence { get; set; }

        public SourceRef() { OcrConfidence = 1.0; }

        public SourceRef(int page, PageBox box, string rawText, double ocrConfidence)
        {
            Page = page; Box = box; RawText = rawText; OcrConfidence = ocrConfidence;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "page {0} {1} \"{2}\"", Page, Box, RawText);
        }
    }
}
