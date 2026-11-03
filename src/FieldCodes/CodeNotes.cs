using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FieldCodes
{
    /// <summary>
    /// Why a code is on an ignore list, and what represents its points instead: a ground shot is
    /// ignored for drafting because the surface is built from it. Recorded with the entry so an
    /// exclusion is documented, never silent.
    /// </summary>
    public sealed class CodeNote
    {
        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("reason", NullValueHandling = NullValueHandling.Ignore)] public string Reason { get; set; }

        /// <summary>What must hold the point instead: "surface" (a TIN built from it), or empty.</summary>
        [JsonProperty("expect", NullValueHandling = NullValueHandling.Ignore)] public string Expect { get; set; }

        [JsonProperty("by", NullValueHandling = NullValueHandling.Ignore)] public string By { get; set; }
        [JsonProperty("at", NullValueHandling = NullValueHandling.Ignore)] public DateTime? At { get; set; }

        [JsonIgnore] public bool HasReason { get { return !string.IsNullOrWhiteSpace(Reason); } }
        [JsonIgnore] public bool ExpectsSurface { get { return string.Equals((Expect ?? string.Empty).Trim(), "surface", StringComparison.OrdinalIgnoreCase); } }
    }

    /// <summary>
    /// A list of bare codes that may carry a note per entry. In JSON each entry is either the code
    /// ("GS", as every file before this was written) or an object with the code and its reason:
    /// <c>{ "code": "GS", "reason": "ground shot: surface data", "expect": "surface" }</c>.
    /// It is still a list of strings to everything that only needs the codes.
    /// </summary>
    public sealed class NotedCodeList : List<string>
    {
        private readonly Dictionary<string, CodeNote> _notes = new Dictionary<string, CodeNote>(StringComparer.OrdinalIgnoreCase);

        public NotedCodeList() { }
        public NotedCodeList(IEnumerable<string> codes) : base(codes ?? Enumerable.Empty<string>()) { }

        /// <summary>The note recorded for an entry exactly as listed, or null.</summary>
        public CodeNote NoteOf(string entry)
        {
            CodeNote note;
            return entry != null && _notes.TryGetValue(entry.Trim(), out note) ? note : null;
        }

        /// <summary>Adds or replaces the note for an entry (the entry itself is added if missing).</summary>
        public void SetNote(CodeNote note)
        {
            if (note == null || string.IsNullOrWhiteSpace(note.Code)) return;
            var code = note.Code.Trim();
            note.Code = code;
            if (!this.Any(c => string.Equals((c ?? string.Empty).Trim(), code, StringComparison.OrdinalIgnoreCase))) Add(code);
            _notes[code] = note;
        }

        public void RemoveNote(string entry)
        {
            if (entry != null) _notes.Remove(entry.Trim());
        }

        /// <summary>The note for the entry that covers <paramref name="code"/> (an entry or the entry
        /// followed by sequence digits: GS covers GS2), from any list -- a plain list has none.</summary>
        public static CodeNote NoteFor(IEnumerable<string> list, string code)
        {
            var noted = list as NotedCodeList;
            if (noted == null || string.IsNullOrWhiteSpace(code)) return null;
            foreach (var entry in noted)
                if (Covers(entry, code)) return noted.NoteOf(entry) ?? new CodeNote { Code = (entry ?? string.Empty).Trim() };
            return null;
        }

        /// <summary>The entry that covers a code, or null.</summary>
        public static string EntryFor(IEnumerable<string> list, string code)
        {
            if (list == null || string.IsNullOrWhiteSpace(code)) return null;
            foreach (var entry in list)
                if (Covers(entry, code)) return entry.Trim();
            return null;
        }

        public static bool Covers(string entry, string code)
        {
            if (string.IsNullOrWhiteSpace(entry) || string.IsNullOrWhiteSpace(code)) return false;
            var e = entry.Trim();
            var c = code.Trim();
            if (c.Length < e.Length || !c.StartsWith(e, StringComparison.OrdinalIgnoreCase)) return false;
            return c.Substring(e.Length).All(char.IsDigit);
        }
    }

    /// <summary>Reads a code list whose entries are strings or <see cref="CodeNote"/> objects, and
    /// writes an entry with a note as an object and one without as a plain string.</summary>
    public sealed class NotedCodeListConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(List<string>).IsAssignableFrom(objectType);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var list = new NotedCodeList();
            if (reader.TokenType == JsonToken.Null) return list;
            var token = JToken.Load(reader);
            var array = token as JArray;
            if (array == null)
            {
                if (token.Type == JTokenType.String) list.Add((string)token);
                return list;
            }
            foreach (var entry in array)
            {
                if (entry == null || entry.Type == JTokenType.Null) continue;
                if (entry.Type == JTokenType.String) { list.Add((string)entry); continue; }
                if (entry.Type != JTokenType.Object) continue;
                var note = entry.ToObject<CodeNote>();
                if (note == null || string.IsNullOrWhiteSpace(note.Code)) continue;
                list.SetNote(note);
            }
            return list;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var list = value as IEnumerable<string>;
            if (list == null) { writer.WriteNull(); return; }
            var noted = value as NotedCodeList;
            writer.WriteStartArray();
            foreach (var entry in list)
            {
                var note = noted == null ? null : noted.NoteOf(entry);
                if (note == null || (string.IsNullOrWhiteSpace(note.Reason) && string.IsNullOrWhiteSpace(note.Expect) &&
                                     string.IsNullOrWhiteSpace(note.By) && !note.At.HasValue))
                    writer.WriteValue(entry);
                else
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("code"); writer.WriteValue(note.Code);
                    if (!string.IsNullOrWhiteSpace(note.Reason)) { writer.WritePropertyName("reason"); writer.WriteValue(note.Reason); }
                    if (!string.IsNullOrWhiteSpace(note.Expect)) { writer.WritePropertyName("expect"); writer.WriteValue(note.Expect); }
                    if (!string.IsNullOrWhiteSpace(note.By)) { writer.WritePropertyName("by"); writer.WriteValue(note.By); }
                    if (note.At.HasValue) { writer.WritePropertyName("at"); writer.WriteValue(note.At.Value); }
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();
        }
    }
}
