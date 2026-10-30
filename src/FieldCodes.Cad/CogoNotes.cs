using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.Civil.DatabaseServices;

using CivilDocument = Autodesk.Civil.ApplicationServices.CivilDocument;

namespace FieldCodes.Cad
{
    /// <summary>
    /// The crew's note for every COGO point, read once per command: from a note-like point
    /// property or the full description when the raw description is only the code (see
    /// FieldCodes.FieldNoteReader). Every command that parses survey points parses through
    /// <see cref="Parse"/>, so a pole shot "PP" with "1234 PSE XFMR" in its note field is the
    /// same pole to FTFLABELS, FTFTAG, FTFTREES and the review grid.
    ///
    /// Point properties (Civil 3D user-defined properties) are read by reflection, as
    /// CogoPointPlotting reads point-group overrides: a member missing in some release means
    /// "no property", never a broken build or a failed command.
    /// </summary>
    internal sealed class CogoNotes
    {
        private readonly FieldNoteReader _reader;
        private readonly Dictionary<ObjectId, PointRecord> _records;

        private CogoNotes(FieldNoteReader reader, Dictionary<ObjectId, PointRecord> records)
        {
            _reader = reader;
            _records = records;
        }

        /// <summary>Reads every point's text in the drawing. Never throws: a drawing whose
        /// properties cannot be read simply has no notes outside the descriptions.</summary>
        public static CogoNotes For(Database db, Transaction tr, FieldCodes.Settings.FtfSettings settings)
        {
            var source = settings != null && settings.Labels != null ? settings.Labels.NoteSource : null;
            source = (source ?? string.Empty).Trim();

            var properties = string.Equals(source, FieldNoteReader.NoneSource, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(source, FieldNoteReader.FullDescriptionSource, StringComparison.OrdinalIgnoreCase)
                ? new List<KeyValuePair<string, object>>()
                : PointProperties(db, source);

            var records = new Dictionary<ObjectId, PointRecord>();
            foreach (var cogo in CadUtil.CogoPointsInOrder(db, tr))
                records[cogo.ObjectId] = Record(cogo, properties);

            return new CogoNotes(new FieldNoteReader(source, records.Values), records);
        }

        /// <summary>The point parsed with its note, when it has one the code takes.</summary>
        public ParsedPoint Parse(FieldCodeParser parser, CogoPoint cogo)
        {
            var number = cogo.PointNumber.ToString(CultureInfo.InvariantCulture);
            PointRecord record;
            if (!_records.TryGetValue(cogo.ObjectId, out record))
                return parser.Parse(number, cogo.RawDescription);

            string from;
            var note = _reader.NoteFor(record, out from);
            return parser.Parse(number, cogo.RawDescription, note);
        }

        /// <summary>One line saying where notes were read from, when any were.</summary>
        public void Report(Editor ed, string command)
        {
            if (_reader.NotesFound == 0) return;
            ed.WriteMessage("\n{0}: crew notes read for {1} point(s) whose description is only the code ({2}). " +
                            "Settings > Point Labels > Read notes from changes where they come from.",
                            command, _reader.NotesFound, string.Join(", ", _reader.Summary().ToArray()));
        }

        // ------------------------------------------------------------------ reading a point

        private static PointRecord Record(CogoPoint cogo, IList<KeyValuePair<string, object>> properties)
        {
            var record = new PointRecord
            {
                Number = cogo.PointNumber.ToString(CultureInfo.InvariantCulture),
                RawDescription = cogo.RawDescription
            };
            try
            {
                record.FullDescription = cogo.FullDescription;
                record.DescriptionFormat = cogo.DescriptionFormat;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { }

            foreach (var property in properties)
            {
                var value = ValueOf(cogo, property.Value);
                if (!string.IsNullOrWhiteSpace(value))
                    record.Properties.Add(new KeyValuePair<string, string>(property.Key, value));
            }
            return record;
        }

        /// <summary>
        /// The drawing's point properties worth reading: the one named in Settings, or (automatic)
        /// every one whose name says it is a note. Reading every property of every point would be
        /// wasted work on a drawing with many.
        /// </summary>
        private static IList<KeyValuePair<string, object>> PointProperties(Database db, string source)
        {
            var wanted = new List<KeyValuePair<string, object>>();
            try
            {
                var civil = CivilDocument.GetCivilDocument(db);
                var member = civil == null ? null : civil.GetType().GetProperty("PointUDPs");
                var all = member == null ? null : member.GetValue(civil, null) as IEnumerable;
                if (all == null) return wanted;

                foreach (var udp in all)
                {
                    if (udp == null) continue;
                    var nameMember = udp.GetType().GetProperty("Name");
                    var name = nameMember == null ? null : nameMember.GetValue(udp, null) as string;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var match = source.Length > 0
                        ? string.Equals(name.Trim(), source, StringComparison.OrdinalIgnoreCase)
                        : FieldNoteReader.IsNoteLikeName(name);
                    if (match) wanted.Add(new KeyValuePair<string, object>(name.Trim(), udp));
                }
            }
            catch (Exception)
            {
                // No readable properties: the descriptions are all there is.
            }
            return wanted;
        }

        private static readonly Dictionary<Type, MethodInfo> Getters = new Dictionary<Type, MethodInfo>();

        /// <summary>CogoPoint.GetUDPValue for the property's own type (string, integer, double...),
        /// as text. Null when the point does not carry it.</summary>
        private static string ValueOf(CogoPoint cogo, object udp)
        {
            MethodInfo getter;
            var type = udp.GetType();
            if (!Getters.TryGetValue(type, out getter))
            {
                getter = typeof(CogoPoint).GetMethod("GetUDPValue", new[] { type });
                Getters[type] = getter;
            }
            if (getter == null) return null;

            try
            {
                var value = getter.Invoke(cogo, new[] { udp });
                return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            catch (TargetInvocationException)
            {
                return null;   // the point's classification does not include this property
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
