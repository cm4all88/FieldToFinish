using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace FieldCodes.Linework
{
    /// <summary>What a Civil 3D linework command does to the figure it follows.</summary>
    public enum LineworkCommandKind
    {
        Begin,
        End,
        Close,
        Continue,
        CurveBegin,
        CurveEnd,
        Rectangle,
        RightTurn,
        Extend,
        Circle,
        PointOnCurve,
        Offset,
        RecallPoint,
        ConnectPoint
    }

    /// <summary>
    /// The office's linework command codes (Civil 3D's linework code set). The defaults are the
    /// PMX-Universal.f2f_xdef set recorded in rules.json: B begin, E end, C close, U continue,
    /// P begin curve, T end curve, G rectangle, RT right turn, X extend, CIR circle, OC point on
    /// curve, HO/VO/SO offsets, RPN recall point, CPN connect point; "/" escapes a word. An
    /// office with another code set lists its own here ("lineworkCommands" in rules.json).
    /// </summary>
    public sealed class LineworkCommandSettings
    {
        [JsonProperty("begin", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> Begin { get; set; }
        [JsonProperty("end", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> End { get; set; }
        [JsonProperty("close", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> Close { get; set; }
        [JsonProperty("continue", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> Continue { get; set; }
        [JsonProperty("curveBegin", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> CurveBegin { get; set; }
        [JsonProperty("curveEnd", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> CurveEnd { get; set; }
        [JsonProperty("rectangle", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> Rectangle { get; set; }
        [JsonProperty("rightTurn", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> RightTurn { get; set; }
        [JsonProperty("extend", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> Extend { get; set; }
        [JsonProperty("circle", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> Circle { get; set; }
        [JsonProperty("pointOnCurve", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> PointOnCurve { get; set; }
        [JsonProperty("offset", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> Offset { get; set; }
        [JsonProperty("recallPoint", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> RecallPoint { get; set; }
        [JsonProperty("connectPoint", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> ConnectPoint { get; set; }

        /// <summary>The character that makes the next word plain text (PMX: "/").</summary>
        [JsonProperty("escape")] public string Escape { get; set; }

        public static LineworkCommandSettings Pmx()
        {
            return new LineworkCommandSettings
            {
                Begin = new List<string> { "B" },
                End = new List<string> { "E" },
                Close = new List<string> { "C" },
                Continue = new List<string> { "U" },
                CurveBegin = new List<string> { "P" },
                CurveEnd = new List<string> { "T" },
                Rectangle = new List<string> { "G" },
                RightTurn = new List<string> { "RT" },
                Extend = new List<string> { "X" },
                Circle = new List<string> { "CIR" },
                PointOnCurve = new List<string> { "OC" },
                Offset = new List<string> { "HO", "VO", "SO" },
                RecallPoint = new List<string> { "RPN" },
                ConnectPoint = new List<string> { "CPN" },
                Escape = "/"
            };
        }

        /// <summary>Token (upper case) → what it does. Later lists win a token listed twice.</summary>
        public IDictionary<string, LineworkCommandKind> Tokens()
        {
            var map = new Dictionary<string, LineworkCommandKind>(StringComparer.OrdinalIgnoreCase);
            Add(map, Begin, LineworkCommandKind.Begin);
            Add(map, End, LineworkCommandKind.End);
            Add(map, Close, LineworkCommandKind.Close);
            Add(map, Continue, LineworkCommandKind.Continue);
            Add(map, CurveBegin, LineworkCommandKind.CurveBegin);
            Add(map, CurveEnd, LineworkCommandKind.CurveEnd);
            Add(map, Rectangle, LineworkCommandKind.Rectangle);
            Add(map, RightTurn, LineworkCommandKind.RightTurn);
            Add(map, Extend, LineworkCommandKind.Extend);
            Add(map, Circle, LineworkCommandKind.Circle);
            Add(map, PointOnCurve, LineworkCommandKind.PointOnCurve);
            Add(map, Offset, LineworkCommandKind.Offset);
            Add(map, RecallPoint, LineworkCommandKind.RecallPoint);
            Add(map, ConnectPoint, LineworkCommandKind.ConnectPoint);
            return map;
        }

        /// <summary>A missing list takes the PMX default, so an older rules file still parses.</summary>
        public void FillMissing()
        {
            var pmx = Pmx();
            if (Begin == null) Begin = pmx.Begin;
            if (End == null) End = pmx.End;
            if (Close == null) Close = pmx.Close;
            if (Continue == null) Continue = pmx.Continue;
            if (CurveBegin == null) CurveBegin = pmx.CurveBegin;
            if (CurveEnd == null) CurveEnd = pmx.CurveEnd;
            if (Rectangle == null) Rectangle = pmx.Rectangle;
            if (RightTurn == null) RightTurn = pmx.RightTurn;
            if (Extend == null) Extend = pmx.Extend;
            if (Circle == null) Circle = pmx.Circle;
            if (PointOnCurve == null) PointOnCurve = pmx.PointOnCurve;
            if (Offset == null) Offset = pmx.Offset;
            if (RecallPoint == null) RecallPoint = pmx.RecallPoint;
            if (ConnectPoint == null) ConnectPoint = pmx.ConnectPoint;
            if (Escape == null) Escape = pmx.Escape;
        }

        /// <summary>Tokens listed for more than one command (a configuration error).</summary>
        public static IList<string> Clashes(LineworkCommandSettings s)
        {
            if (s == null) return new List<string>();
            var lists = new[] { s.Begin, s.End, s.Close, s.Continue, s.CurveBegin, s.CurveEnd, s.Rectangle, s.RightTurn,
                                s.Extend, s.Circle, s.PointOnCurve, s.Offset, s.RecallPoint, s.ConnectPoint };
            return lists.Where(l => l != null)
                        .SelectMany(l => l.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim().ToUpperInvariant()).Distinct())
                        .GroupBy(t => t)
                        .Where(g => g.Count() > 1)
                        .Select(g => g.Key)
                        .ToList();
        }

        private static void Add(Dictionary<string, LineworkCommandKind> map, IEnumerable<string> tokens, LineworkCommandKind kind)
        {
            foreach (var t in tokens ?? Enumerable.Empty<string>())
                if (!string.IsNullOrWhiteSpace(t)) map[t.Trim()] = kind;
        }
    }

    /// <summary>One linework command as written: "B", "HO 2.5", "CPN 10234".</summary>
    public sealed class LineworkCommand
    {
        public LineworkCommandKind Kind { get; set; }
        public string Token { get; set; }
        /// <summary>The numbers that belong to the command (offset distance, point numbers).</summary>
        public IList<string> Parameters { get; set; }

        public LineworkCommand() { Parameters = new List<string>(); }

        public override string ToString()
        {
            return Parameters.Count == 0 ? Token : Token + " " + string.Join(" ", Parameters.ToArray());
        }
    }

    /// <summary>One figure named in a description, with what the description says about it.</summary>
    public sealed class FigureCoding
    {
        /// <summary>The figure as written, upper case, its number kept: "FNC1". Two strings of one
        /// code (FNC and FNC1, EC1 and EC2) are different figures.</summary>
        public string FigureId { get; set; }
        /// <summary>The code without the figure number: "FNC".</summary>
        public string Code { get; set; }
        /// <summary>The figure number as written ("1"), or empty.</summary>
        public string Number { get; set; }
        public IList<LineworkCommand> Commands { get; set; }
        /// <summary>Numbers after the figure that no command takes (a fence height: "FNC B 3.5").</summary>
        public IList<string> Values { get; set; }
        /// <summary>Other words after the figure, as written: notes, or codes the office has not
        /// listed. Escaped words come here without their escape character.</summary>
        public IList<string> Words { get; set; }
        /// <summary>LEFT/RIGHT written after the figure (L, LEFT, R, RIGHT), when written.</summary>
        public LineLabelSide? Side { get; set; }

        public FigureCoding()
        {
            Commands = new List<LineworkCommand>();
            Values = new List<string>();
            Words = new List<string>();
        }

        public bool Has(LineworkCommandKind kind) { return Commands.Any(c => c.Kind == kind); }
        public bool Begins { get { return Has(LineworkCommandKind.Begin); } }
        public bool Ends { get { return Has(LineworkCommandKind.End); } }
        public bool Closes { get { return Has(LineworkCommandKind.Close); } }

        public override string ToString()
        {
            var parts = new List<string> { FigureId };
            parts.AddRange(Commands.Select(c => c.ToString()));
            parts.AddRange(Values);
            parts.AddRange(Words);
            return string.Join(" ", parts.ToArray());
        }
    }

    /// <summary>A whole description read as linework coding.</summary>
    public sealed class LineworkShot
    {
        public string Description { get; set; }
        /// <summary>Words before the first figure: a point code ("FGP" in "FGP FNC") and its notes.</summary>
        public IList<string> Leading { get; set; }
        /// <summary>Every figure the shot is on, in the order written ("BLD B EC B BLD1 B" is on three).</summary>
        public IList<FigureCoding> Figures { get; set; }

        public LineworkShot()
        {
            Leading = new List<string>();
            Figures = new List<FigureCoding>();
        }

        public bool IsLinework { get { return Figures.Count > 0; } }

        public FigureCoding Figure(string figureId)
        {
            return Figures.FirstOrDefault(f => string.Equals(f.FigureId, figureId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Reads a raw description as Civil 3D linework coding: which figures the shot is on (with their
    /// numbers), and for each, its commands (begin, end, close...), their parameters, the numbers left
    /// over (heights), a side, and any other words. Nothing is guessed: a word that is neither a
    /// linework code, a command, a number nor a side is kept as a word.
    ///
    /// Ambiguous tokens are resolved by position. A token that is both a figure code and a command
    /// (CIR) is a command after a figure, a figure at the start. A linework code that is not a
    /// command (EC, which other code sets use for "end curve") starts a new figure.
    /// </summary>
    public static class LineworkCoding
    {
        private static readonly Regex Numeric = new Regex(@"^-?[0-9]+(?:\.[0-9]+)?(?:'|FT)?$",
                                                          RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly HashSet<LineworkCommandKind> TakesParameters = new HashSet<LineworkCommandKind>
        {
            LineworkCommandKind.Offset, LineworkCommandKind.RecallPoint, LineworkCommandKind.ConnectPoint,
            LineworkCommandKind.Rectangle, LineworkCommandKind.Circle
        };

        public static LineworkShot Parse(string description, ICollection<string> lineworkCodes,
                                         LineworkCommandSettings commands = null)
        {
            var shot = new LineworkShot { Description = description };
            if (string.IsNullOrWhiteSpace(description)) return shot;
            if (commands == null) commands = LineworkCommandSettings.Pmx();
            var tokens = commands.Tokens();
            var codes = new HashSet<string>((lineworkCodes ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c))
                                                                                  .Select(c => c.Trim()),
                                            StringComparer.OrdinalIgnoreCase);
            var escape = string.IsNullOrEmpty(commands.Escape) ? null : commands.Escape;

            FigureCoding figure = null;
            LineworkCommand last = null;
            foreach (var raw in description.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var token = raw.ToUpperInvariant();

                // An escaped word is plain text, whatever it looks like.
                if (escape != null && token.StartsWith(escape, StringComparison.Ordinal) && token.Length > escape.Length)
                {
                    Word(shot, figure, raw.Substring(escape.Length));
                    last = null;
                    continue;
                }

                LineworkCommandKind kind;
                var isCommand = tokens.TryGetValue(token, out kind);
                var isCode = IsFigureCode(token, codes);

                if (figure != null && isCommand)
                {
                    last = new LineworkCommand { Kind = kind, Token = token };
                    figure.Commands.Add(last);
                    continue;
                }

                if (isCode)
                {
                    figure = NewFigure(token);
                    shot.Figures.Add(figure);
                    last = null;
                    continue;
                }

                if (Numeric.IsMatch(token))
                {
                    if (last != null && TakesParameters.Contains(last.Kind)) last.Parameters.Add(token);
                    else if (figure != null) figure.Values.Add(token);
                    else shot.Leading.Add(raw);
                    continue;
                }

                last = null;
                var side = SideOf(token);
                if (figure != null && side.HasValue && !figure.Side.HasValue) { figure.Side = side; continue; }
                Word(shot, figure, raw);
            }
            return shot;
        }

        /// <summary>Parse with the rules' own linework codes and command set.</summary>
        public static LineworkShot Parse(string description, RulesConfig rules)
        {
            return Parse(description, rules == null ? null : rules.LineworkCodes, rules == null ? null : rules.LineworkCommands);
        }

        /// <summary>True when the token is a listed linework code, a figure number allowed (FNC1).</summary>
        public static bool IsFigureCode(string token, ICollection<string> codes)
        {
            if (string.IsNullOrEmpty(token) || codes == null || codes.Count == 0) return false;
            var bare = Bare(token);
            return bare.Length > 0 && codes.Contains(bare);
        }

        private static FigureCoding NewFigure(string token)
        {
            var bare = Bare(token);
            return new FigureCoding { FigureId = token, Code = bare, Number = token.Substring(bare.Length) };
        }

        /// <summary>The code without trailing figure digits ("FNC12" gives FNC; "123" stays "123").</summary>
        private static string Bare(string token)
        {
            var end = token.Length;
            while (end > 1 && char.IsDigit(token[end - 1])) end--;
            return token.Substring(0, end);
        }

        private static LineLabelSide? SideOf(string token)
        {
            switch (token)
            {
                case "L":
                case "LEFT": return LineLabelSide.Left;
                case "R":
                case "RIGHT": return LineLabelSide.Right;
                default: return null;
            }
        }

        private static void Word(LineworkShot shot, FigureCoding figure, string word)
        {
            if (figure != null) figure.Words.Add(word);
            else shot.Leading.Add(word);
        }
    }
}
