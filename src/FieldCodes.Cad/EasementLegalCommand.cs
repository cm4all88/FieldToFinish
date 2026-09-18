using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Easements;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// FTFEASEMENTLEGAL: writes a DRAFT legal description for a strip easement (and its
    /// temporary construction easement) from the stored geometry, in the office's
    /// Exhibit A pattern. The drafter supplies the parcel and the names of the corners and
    /// lines; the draft is saved beside the drawing and marked for surveyor review.
    /// </summary>
    public sealed class EasementLegalCommand
    {
        /// <summary>One blank the drafter fills in, and whether this easement needs it.</summary>
        internal sealed class LegalField
        {
            public string Caption;
            public string Hint;
            public Func<LegalInputs, string> Get;
            public Action<LegalInputs, string> Set;
            public bool Multiline;
        }

        /// <summary>The blanks this easement's description uses, in the order they are read.</summary>
        internal static List<LegalField> FieldsFor(EasementRecord e)
        {
            var fields = BaseFields(e);
            if (e.Components == null || e.Components.Count == 0) return fields;

            // Each component names its own parcel, and for a clicked area its corners and followed lines.
            var at = fields.FindIndex(f => f.Caption == "County");
            var extra = new List<LegalField>();
            foreach (var c in e.Components)
            {
                var label = c.Label;
                Func<LegalInputs, LegalInputs> of = i =>
                {
                    if (i.ComponentInputs == null) i.ComponentInputs = new Dictionary<string, LegalInputs>();
                    LegalInputs ci;
                    if (!i.ComponentInputs.TryGetValue(label, out ci)) { ci = new LegalInputs(); i.ComponentInputs[label] = ci; }
                    return ci;
                };
                extra.Add(new LegalField
                {
                    Caption = "Component " + label + (c.Kind == EasementComponent.PortionKind ? ": of (the lot)" : c.Kind == EasementComponent.AreaKind ? ": that portion of" : ": description"),
                    Hint = c.Kind == EasementComponent.BoundaryKind ? "THAT PORTION OF LOT 3 ... DESCRIBED AS FOLLOWS: ..." : "LOT 3, SHORT PLAT NO. ...",
                    Get = i => of(i).ParcelDescription, Set = (i, v) => of(i).ParcelDescription = v, Multiline = true
                });
                if (c.Kind != EasementComponent.AreaKind) continue;
                if (c.PointOfCommencement != null)
                    extra.Add(new LegalField { Caption = "Component " + label + ": commencing at", Hint = "THE NORTHWEST CORNER OF SAID LOT 3", Get = i => of(i).CommencementCorner, Set = (i, v) => of(i).CommencementCorner = v });
                else
                    extra.Add(new LegalField { Caption = "Component " + label + ": beginning at", Hint = "THE NORTHWEST CORNER OF SAID LOT 3", Get = i => of(i).BeginningDescription, Set = (i, v) => of(i).BeginningDescription = v });
                for (var s = 0; s < (c.AreaSides ?? new List<AreaSide>()).Count; s++)
                {
                    if (string.IsNullOrWhiteSpace(c.AreaSides[s].FollowHandle)) continue;
                    var key = (s + 1).ToString(CultureInfo.InvariantCulture);
                    extra.Add(new LegalField
                    {
                        Caption = "Component " + label + ": side " + key + " runs along", Hint = "THE SOUTH LINE OF SAID LOT 3",
                        Get = i => { string v = null; var ci = of(i); if (ci.SideLines != null) ci.SideLines.TryGetValue(key, out v); return v; },
                        Set = (i, v) => { var ci = of(i); if (ci.SideLines == null) ci.SideLines = new Dictionary<string, string>(); if (string.IsNullOrWhiteSpace(v)) ci.SideLines.Remove(key); else ci.SideLines[key] = v; }
                    });
                }
            }
            fields.InsertRange(at < 0 ? fields.Count : at, extra);
            return fields;
        }

        private static List<LegalField> BaseFields(EasementRecord e)
        {
            if (e.IsPortion) return PortionFields();
            if (e.IsArea) return AreaFields(e);
            var fields = new List<LegalField>
            {
                new LegalField { Caption = "Exhibit", Hint = "EXHIBIT A", Get = i => i.ExhibitLabel, Set = (i, v) => i.ExhibitLabel = v },
                new LegalField
                {
                    Caption = "That portion of", Hint = "PARCEL 3, SNOHOMISH COUNTY SHORT PLAT NO. SP-330 (7-79), RECORDED UNDER AUDITOR'S FILE NUMBER ..., BEING A PORTION OF ...",
                    Get = i => i.ParcelDescription, Set = (i, v) => i.ParcelDescription = v, Multiline = true
                }
            };
            if (e.PointOfCommencement != null)
                fields.Add(new LegalField { Caption = "Commencing at", Hint = "THE SOUTHEAST CORNER OF SAID PARCEL 2", Get = i => i.CommencementCorner, Set = (i, v) => i.CommencementCorner = v });
            if (e.PointOfCommencement != null && e.CommencementAlong != null && (e.BeginsOn == null || e.BeginsOn.Handle != e.CommencementAlong.Handle))
                fields.Add(new LegalField { Caption = "Tie runs along", Hint = "THE SOUTH LINE OF SAID PARCEL 2", Get = i => i.TieLine, Set = (i, v) => i.TieLine = v });
            if (e.PointOfCommencement == null)
                fields.Add(new LegalField { Caption = "Beginning at", Hint = "A POINT ON THE SOUTH LINE OF SAID PARCEL 2, ...", Get = i => i.BeginningDescription, Set = (i, v) => i.BeginningDescription = v, Multiline = true });
            if (e.BeginsOn != null)
                fields.Add(new LegalField { Caption = "Point of Beginning is on", Hint = "THE SOUTH LINE OF SAID PARCEL 2", Get = i => i.BeginningLine, Set = (i, v) => i.BeginningLine = v });
            if (e.EndsOn != null)
                fields.Add(new LegalField { Caption = "Terminus is on", Hint = "THE WEST LINE OF SAID PARCEL 2", Get = i => i.TerminusLine, Set = (i, v) => i.TerminusLine = v });
            if (e.TerminusTie != null)
                fields.Add(new LegalField { Caption = "Terminus is tied to", Hint = "THE NORTHWEST CORNER OF SAID PARCEL 4", Get = i => i.TerminusCorner, Set = (i, v) => i.TerminusCorner = v });
            fields.Add(new LegalField { Caption = "County", Hint = "SNOHOMISH", Get = i => i.County, Set = (i, v) => i.County = v });
            fields.Add(new LegalField { Caption = "State", Hint = "WASHINGTON", Get = i => i.State, Set = (i, v) => i.State = v });
            return fields;
        }

        private static List<LegalField> PortionFields()
        {
            return new List<LegalField>
            {
                new LegalField { Caption = "Exhibit", Hint = "EXHIBIT A", Get = i => i.ExhibitLabel, Set = (i, v) => i.ExhibitLabel = v },
                new LegalField
                {
                    Caption = "Of (the lot)", Hint = "LOT 2, SNOHOMISH COUNTY SHORT PLAT NO. ..., RECORDED UNDER AUDITOR'S FILE NUMBER ...",
                    Get = i => i.ParcelDescription, Set = (i, v) => i.ParcelDescription = v, Multiline = true
                },
                new LegalField { Caption = "County", Hint = "SNOHOMISH", Get = i => i.County, Set = (i, v) => i.County = v },
                new LegalField { Caption = "State", Hint = "WASHINGTON", Get = i => i.State, Set = (i, v) => i.State = v }
            };
        }

        private static List<LegalField> AreaFields(EasementRecord e)
        {
            var fields = new List<LegalField>
            {
                new LegalField { Caption = "Exhibit", Hint = "EXHIBIT A", Get = i => i.ExhibitLabel, Set = (i, v) => i.ExhibitLabel = v },
                new LegalField
                {
                    Caption = "That portion of", Hint = "LOT 2, SNOHOMISH COUNTY SHORT PLAT NO. ..., BEING A PORTION OF ...",
                    Get = i => i.ParcelDescription, Set = (i, v) => i.ParcelDescription = v, Multiline = true
                }
            };
            if (e.PointOfCommencement != null)
                fields.Add(new LegalField { Caption = "Commencing at", Hint = "THE NORTHWEST CORNER OF SAID LOT 2", Get = i => i.CommencementCorner, Set = (i, v) => i.CommencementCorner = v });
            else
                fields.Add(new LegalField { Caption = "Beginning at", Hint = "THE NORTHWEST CORNER OF SAID LOT 2", Get = i => i.BeginningDescription, Set = (i, v) => i.BeginningDescription = v, Multiline = true });
            var sides = e.AreaSides ?? new List<AreaSide>();
            for (var s = 0; s < sides.Count; s++)
            {
                if (string.IsNullOrWhiteSpace(sides[s].FollowHandle)) continue;
                var key = (s + 1).ToString(CultureInfo.InvariantCulture);
                fields.Add(new LegalField
                {
                    Caption = "Side " + key + " runs along", Hint = "THE SOUTH LINE OF SAID LOT 2",
                    Get = i => { string v = null; if (i.SideLines != null) i.SideLines.TryGetValue(key, out v); return v; },
                    Set = (i, v) =>
                    {
                        if (i.SideLines == null) i.SideLines = new Dictionary<string, string>();
                        if (string.IsNullOrWhiteSpace(v)) i.SideLines.Remove(key); else i.SideLines[key] = v;
                    }
                });
            }
            fields.Add(new LegalField { Caption = "County", Hint = "SNOHOMISH", Get = i => i.County, Set = (i, v) => i.County = v });
            fields.Add(new LegalField { Caption = "State", Hint = "WASHINGTON", Get = i => i.State, Set = (i, v) => i.State = v });
            return fields;
        }

        /// <summary>What the window needs to show and write the draft.</summary>
        internal sealed class LegalSession
        {
            public EasementRecord Easement;
            public List<LegalField> Fields;
            public LegalInputs Inputs;
            public Func<LegalInputs, LegalDraft> Write;
        }

        [CommandMethod("FTFEASEMENTLEGAL", CommandFlags.Modal)]
        public void WriteLegal()
        {
            FtfSession.Run("FTFEASEMENTLEGAL", (db, tr, ed) =>
            {
                var settings = FtfSession.SettingsFor(db, FtfSession.Rules(db));
                var es = settings.Easements;
                var records = DrawingStore.LoadEasements(db, tr);
                var permanent = records.Where(r => !r.IsTemporary).ToList();
                if (permanent.Count == 0) { ed.WriteMessage("\nFTFEASEMENTLEGAL: no strip easements are stored in this drawing.\n"); return; }

                var easement = permanent.Count == 1 ? permanent[0] : Pick(ed, tr, records);
                if (easement == null) { ed.WriteMessage("\nFTFEASEMENTLEGAL: cancelled.\n"); return; }
                var temporary = easement.GroupId == null ? null
                    : records.FirstOrDefault(r => r.IsTemporary && r.GroupId == easement.GroupId);
                ed.WriteMessage("\n  {0}{1}", easement.Title, temporary != null ? " with its " + temporary.Title : string.Empty);
                if (!easement.Trimmable && !easement.IsPortion && !easement.IsArea)
                    ed.WriteMessage("\n  ! This easement was made before ties and trim lines were recorded; its draft has fewer blanks filled from the drawing.");

                var changes = EasementCommands.Changes(db, tr, easement).ToList();
                if (temporary != null) changes.AddRange(EasementCommands.Changes(db, tr, temporary).Where(c => !changes.Contains(c)));

                // Start from what was entered last time, or the parcel and county used on another easement here.
                var inputs = easement.Legal != null ? easement.Legal.Copy() : new LegalInputs();
                if (easement.Legal == null)
                {
                    var previous = records.Where(r => r.Legal != null).OrderByDescending(r => r.CreatedUtc).Select(r => r.Legal).FirstOrDefault();
                    if (previous != null)
                    {
                        inputs.ParcelDescription = previous.ParcelDescription;
                        inputs.County = previous.County;
                        inputs.State = previous.State;
                        inputs.ExhibitLabel = previous.ExhibitLabel;
                    }
                }

                // The legal draft and the drawing use the same record. Before a draft is offered for
                // review, its stated courses are traversed on their own and must reproduce the CAD easement.
                var closure = Closure.ForRecord(easement, es);
                var notReady = closure.Problems.ToList();
                if (temporary != null) notReady.AddRange(Closure.ForRecord(temporary, es).Problems.Select(p => "Temporary construction easement -- " + p));
                foreach (var p in notReady) ed.WriteMessage("\n  X " + p);

                var session = new LegalSession
                {
                    Easement = easement, Fields = FieldsFor(easement), Inputs = inputs,
                    Write = i =>
                    {
                        var d = easement.IsPortion ? LegalDescriptionWriter.WritePortion(easement, i, es, changes)
                              : easement.IsArea ? LegalDescriptionWriter.WriteArea(easement, i, es, changes)
                              : LegalDescriptionWriter.Write(easement, temporary, i, es, changes);
                        LegalDescriptionWriter.MarkNotReady(d, notReady);
                        return d;
                    }
                };
                if (!(Headless() ? AskOnCommandLine(ed, session) : ShowWindow(session)))
                {
                    ed.WriteMessage("\nFTFEASEMENTLEGAL: cancelled -- nothing saved.\n");
                    return;
                }

                var draft = session.Write(session.Inputs);
                easement.Legal = session.Inputs;
                easement.LegalStatus = draft.ReadyForReview
                    ? "DRAFT WRITTEN " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " - requires surveyor review before use"
                    : "DRAFT NOT READY FOR REVIEW " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
                      " - the stated courses do not reproduce the CAD easement: " + notReady[0];
                DrawingStore.SaveEasement(db, tr, easement);

                var path = SaveBesideDrawing(db, easement, draft);
                ed.WriteMessage(path != null
                    ? "\nFTFEASEMENTLEGAL: draft written to " + path
                    : "\nFTFEASEMENTLEGAL: the drawing has not been saved, so the draft was not written to a file.");
                foreach (var check in draft.Checks) ed.WriteMessage("\n  ? " + check);
                ed.WriteMessage("\n");
            });
        }

        private static EasementRecord Pick(Editor ed, Transaction tr, IList<EasementRecord> records)
        {
            while (true)
            {
                var picked = ed.GetEntity(new PromptEntityOptions("\nSelect the easement -- its outline, hatch or a label: "));
                if (picked.Status != PromptStatus.OK) return null;
                var stamp = Ownership.Read((AcEntity)tr.GetObject(picked.ObjectId, OpenMode.ForRead));
                var record = stamp == null ? null : records.FirstOrDefault(r => r.Id == stamp.PointNumber);
                if (record == null) { ed.WriteMessage("\n  That is not part of a stored strip easement."); continue; }
                if (!record.IsTemporary) return record;
                var permanent = records.FirstOrDefault(r => !r.IsTemporary && r.GroupId != null && r.GroupId == record.GroupId);
                if (permanent != null) return permanent;
                ed.WriteMessage("\n  That temporary construction easement's easement is no longer stored.");
            }
        }

        private static bool AskOnCommandLine(Editor ed, LegalSession session)
        {
            foreach (var field in session.Fields)
            {
                var current = field.Get(session.Inputs);
                var answer = ed.GetString(new PromptStringOptions("\n" + field.Caption +
                    (string.IsNullOrWhiteSpace(current) ? " (e.g. " + field.Hint + ")" : " <" + current + ">") + ": ") { AllowSpaces = true });
                if (answer.Status == PromptStatus.Cancel) return false;
                if (answer.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(answer.StringResult))
                    field.Set(session.Inputs, answer.StringResult.Trim().ToUpperInvariant());
            }
            return true;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool ShowWindow(LegalSession session)
        {
            using (var form = new Ui.LegalDescriptionForm(session))
                return AcadApp.ShowModalDialog(form) == System.Windows.Forms.DialogResult.OK;
        }

        private static bool Headless()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().ProcessName.StartsWith("accoreconsole", StringComparison.OrdinalIgnoreCase); }
            catch (InvalidOperationException) { return false; }
        }

        internal static string SaveBesideDrawing(Database db, EasementRecord easement, LegalDraft draft)
        {
            if (string.IsNullOrWhiteSpace(db.Filename) || !File.Exists(db.Filename)) return null;
            var safe = new string(easement.Title.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
            while (safe.Contains("--")) safe = safe.Replace("--", "-");
            var path = Path.Combine(Path.GetDirectoryName(db.Filename),
                                    Path.GetFileNameWithoutExtension(db.Filename) + "." + safe + ".legal-draft.txt");
            var text = new StringBuilder(draft.Text);
            if (draft.Checks.Count > 0)
            {
                text.AppendLine().AppendLine("----------------------------------------------------------------");
                text.AppendLine("CHECK BEFORE USE:");
                foreach (var check in draft.Checks) text.AppendLine("- " + check);
            }
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
            return path;
        }
    }
}
