using System;
using System.Linq;
using FieldCodes.Annotation;

namespace FieldCodes.Standards
{
    /// <summary>The one lookup of a point's drafting requirement, for every reader (accountability,
    /// Label All, FTFORIENT), so they agree.</summary>
    public static class FeatureRequirements
    {
        /// <summary>The requirement for a parsed point. <paramref name="note"/>: the crew's note
        /// when one was read (it can name a sign's category).</summary>
        public static EffectiveRequirement For(RulesConfig rules, ParsedPoint parsed, string rawDescription, string note = null)
        {
            var standard = rules != null && rules.FeatureStandard != null ? rules.FeatureStandard : FeatureStandard.Pmx();
            return standard.Resolve(SubjectFor(rules, parsed, rawDescription, note));
        }

        /// <summary>
        /// True when the point's own Civil 3D label -- which shows its description -- says all an FTF
        /// label would: no rule writes a label for it ("HOUSE NUMBER 4735 SIGN"), or the label it
        /// writes is the description itself (a bare "SN"). False when FTF writes something more
        /// finished ("SN STOP" becomes "STOP SIGN", a crew note becomes the label): the point label
        /// does not carry that, so the FTF label is still wanted. Label All and point accountability
        /// both ask this before letting a point label stand in for a label (label PointLabel).
        /// </summary>
        public static bool PointLabelSaysIt(ParsedPoint parsed, string rawDescription)
        {
            if (parsed == null || parsed.HasErrors || string.IsNullOrWhiteSpace(parsed.LabelText)) return true;
            if (!string.IsNullOrWhiteSpace(parsed.Note)) return false;
            return string.Equals(Words(parsed.LabelText.Replace("\\P", " ")), Words(rawDescription ?? parsed.RawDescription), StringComparison.OrdinalIgnoreCase);
        }

        private static string Words(string text)
        {
            return string.Join(" ", (text ?? string.Empty).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        public static FeatureSubject SubjectFor(RulesConfig rules, ParsedPoint parsed, string rawDescription, string note)
        {
            var raw = rawDescription ?? (parsed != null ? parsed.RawDescription : null) ?? string.Empty;
            var token = FieldCodeParser.FirstToken(raw.Trim());
            var code = parsed != null && !string.IsNullOrWhiteSpace(parsed.Code) ? parsed.Code : token;
            var known = parsed != null && (parsed.Recognized || parsed.Ignored || parsed.NoContent);
            if (!known) known = AnnotationPolicy.KnowsCode(code);
            if (!known && rules != null && rules.ControlCodes != null)
                known = rules.ControlCodes.Any(c => string.Equals((c ?? string.Empty).Trim(), token, StringComparison.OrdinalIgnoreCase));
            if (!known && rules != null && rules.FeatureStandard != null)
                known = rules.FeatureStandard.Requirements.Any(r => r != null && r.Enabled && r.MatchesCode(FeatureStandard.BaseCode(code)));
            return new FeatureSubject
            {
                Code = code,
                Family = AnnotationPolicy.CategoryOf(parsed ?? new ParsedPoint { Code = code, RawDescription = raw }, rules),
                Description = raw,
                Note = note ?? (parsed != null ? parsed.Note : null),
                KnownCode = known
            };
        }
    }
}
