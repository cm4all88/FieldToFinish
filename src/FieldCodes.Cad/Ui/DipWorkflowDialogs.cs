using System;
using System.Drawing;
using System.Windows.Forms;
using FieldCodes.Utilities;

namespace FieldCodes.Cad.Ui
{
    // Small modal dialogs that move decisions out of the command line and into the
    // window. Each asks exactly one question and keeps the existing meanings.

    /// <summary>Base plumbing: the family's look, without repeating it three times.</summary>
    internal abstract class DipDialog : Form
    {
        protected DipDialog(string title, int width, int height)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(width, height);
            BackColor = DipBuilderForm.Ground;
            ForeColor = DipBuilderForm.Ink;
            Font = DipBuilderForm.F(9.5f, false);
        }

        protected Label Note(string text, int x, int y, int width, bool heavy = false, Color? colour = null)
        {
            var label = new Label
            {
                Text = text, Left = x, Top = y, Width = width, AutoSize = false, Height = 0,
                Font = DipBuilderForm.F(heavy ? 10f : 9.5f, heavy),
                ForeColor = colour ?? DipBuilderForm.Ink
            };
            label.Height = TextRenderer.MeasureText(text, label.Font, new Size(width, 0),
                                                    TextFormatFlags.WordBreak).Height + 4;
            Controls.Add(label);
            return label;
        }

        protected Button Action(string text, DialogResult result, int x, int y, int width, bool primary = false)
        {
            var button = new Button
            {
                Text = text, Left = x, Top = y, Width = width, Height = 30,
                DialogResult = result, FlatStyle = FlatStyle.Flat,
                BackColor = primary ? DipBuilderForm.Accent : DipBuilderForm.Surface,
                ForeColor = primary ? Color.White : DipBuilderForm.Ink,
                Font = DipBuilderForm.F(9.5f, true), Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = primary ? DipBuilderForm.Accent : DipBuilderForm.ButtonBorder;
            Controls.Add(button);
            return button;
        }
    }

    /// <summary>
    /// Why the drafter chose this structure. Optional -- an empty reason is fine and
    /// the connection is still recorded as the drafter's decision either way. The
    /// point of asking here is that the command line no longer interrupts the pick.
    /// </summary>
    internal sealed class ManualConnectionReasonDialog : DipDialog
    {
        private readonly TextBox _reason;

        public string Reason { get { return _reason.Text.Trim().Length == 0 ? null : _reason.Text.Trim(); } }

        public ManualConnectionReasonDialog(string pipeDescription)
            : base("Connect this pipe", 460, 232)
        {
            Note(pipeDescription, 16, 14, 428, true);
            Note("You are about to click the structure this pipe runs to. It will be recorded as a " +
                 "drafter-selected connection -- not as a field observation, and with no confidence claimed for it.",
                 16, 42, 428, false, DipBuilderForm.Muted);

            Note("Reason (optional)", 16, 106, 428);
            _reason = new TextBox { Left = 16, Top = 128, Width = 428, BackColor = DipBuilderForm.Surface,
                                    ForeColor = DipBuilderForm.Ink, BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(_reason);

            var pick = Action("Pick structure...", DialogResult.OK, 232, 176, 130, true);
            var cancel = Action("Cancel", DialogResult.Cancel, 372, 176, 72);
            AcceptButton = pick;
            CancelButton = cancel;
        }
    }

    /// <summary>
    /// What to do about CAD geometry already sitting between the two structures.
    /// The same four answers FTFDIPDRAW has always asked for, with what was found
    /// spelled out so the decision can be made without going to look.
    /// </summary>
    internal sealed class ExistingPipeDialog : DipDialog
    {
        public ExistingPipeDecision Decision { get; private set; }

        public ExistingPipeDialog(string route, int count, bool ownedByFtf, string layers)
            : base("Existing pipe found", 520, 306)
        {
            Decision = ExistingPipeDecision.Keep;

            Note(route, 16, 14, 488, true);
            Note(count + " existing pipe object(s) found between these structures — " +
                 (ownedByFtf ? "drafted by FTF." : "drawn by hand.") +
                 (string.IsNullOrWhiteSpace(layers) ? string.Empty : "   Layer: " + layers),
                 16, 42, 488, false, DipBuilderForm.Muted);

            Note("Keep existing leaves the drawing alone and draws nothing. Adopt takes hand-drawn " +
                 "geometry under management and labels it, without changing its geometry. Replace erases " +
                 "what is there. Create new draws the FTF pipe alongside it.",
                 16, 78, 488, false, DipBuilderForm.Muted);

            var keep = Action("Keep existing", DialogResult.OK, 16, 158, 150, true);
            var adopt = Action("Adopt / update existing", DialogResult.OK, 176, 158, 170);
            var replace = Action("Replace with FTF pipe", DialogResult.OK, 16, 196, 150);
            var create = Action("Create new alongside", DialogResult.OK, 176, 196, 170);

            keep.Click += (s, e) => Decision = ExistingPipeDecision.Keep;
            adopt.Click += (s, e) => Decision = ExistingPipeDecision.AdoptOrUpdate;
            replace.Click += (s, e) => Decision = ExistingPipeDecision.Replace;
            create.Click += (s, e) => Decision = ExistingPipeDecision.CreateNew;

            Note("Replace erases CAD geometry. Hand-drawn work is never erased unless you choose it here.",
                 16, 240, 488, false, DipBuilderForm.Warn);

            AcceptButton = keep;
            CancelButton = keep;   // closing the dialog keeps what is there
        }
    }

    /// <summary>
    /// The pipe label's text. The generated text is shown beside the box so an
    /// override is a visible choice rather than something that quietly happened.
    /// </summary>
    internal sealed class PipeLabelTextDialog : DipDialog
    {
        private readonly TextBox _text;
        private readonly string _generated;

        public string LabelText { get { return _text.Text; } }

        public PipeLabelTextDialog(string generated, string current)
            : base("Pipe label text", 500, 268)
        {
            _generated = generated ?? string.Empty;

            Note("Generated from the observations", 16, 14, 468, true);
            Note(string.IsNullOrWhiteSpace(generated) ? "(nothing to generate yet)" : generated,
                 16, 40, 468, false, DipBuilderForm.Muted);

            Note("Label text", 16, 80, 468);
            _text = new TextBox { Left = 16, Top = 102, Width = 468, BackColor = DipBuilderForm.Surface,
                                  ForeColor = DipBuilderForm.Ink, BorderStyle = BorderStyle.FixedSingle,
                                  Text = current ?? string.Empty };
            Controls.Add(_text);

            Note("Leave it as the generated text and it keeps updating with the observations. " +
                 "Change it and your text is kept, and a redraw will not overwrite it.",
                 16, 132, 468, false, DipBuilderForm.Muted);

            var reset = Action("Use generated", DialogResult.None, 16, 200, 120);
            reset.Click += (s, e) => _text.Text = _generated;

            var ok = Action("OK", DialogResult.OK, 328, 200, 72, true);
            var cancel = Action("Cancel", DialogResult.Cancel, 410, 200, 74);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
