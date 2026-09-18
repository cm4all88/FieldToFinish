using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldCodes.Cad
{
    /// <summary>One stage of the finishing pass.</summary>
    public sealed class FtfStep
    {
        /// <summary>Command name, which is also how the step is selected.</summary>
        public string Name { get; set; }

        /// <summary>What a surveyor calls this stage -- what the window shows.</summary>
        public string Title { get; set; }

        /// <summary>What it does, for the interface to show.</summary>
        public string Description { get; set; }

        internal Action Run { get; set; }
    }

    /// <summary>What happened to one step.</summary>
    public sealed class FtfStepOutcome
    {
        public string Name { get; set; }
        public bool Succeeded { get; set; }
        public string Error { get; set; }
    }

    public sealed class FtfRunResult
    {
        public IList<FtfStepOutcome> Outcomes { get; set; }
        public bool StoppedEarly { get; set; }

        public FtfRunResult() { Outcomes = new List<FtfStepOutcome>(); }

        public int Completed { get { return Outcomes.Count(o => o.Succeeded); } }
        public int Failed { get { return Outcomes.Count(o => !o.Succeeded); } }
    }

    /// <summary>
    /// The finishing pass, as a service rather than a command.
    ///
    /// FTF sits AFTER Civil 3D's own field-to-finish work. Civil 3D builds the points,
    /// applies description keys, places the survey symbols and creates the coded
    /// linework. This pipeline polishes that result into the office standard: labels,
    /// conflict avoidance, leaders, tags, driplines, schedules and draw order.
    ///
    /// Exposed as a class so the main window can list the steps, let someone run a
    /// subset, and show progress -- rather than the sequence being buried in a command
    /// that only ever runs all of it.
    ///
    /// Order is not arbitrary. Driplines and labels both create entities that change
    /// what draw-order banding sees, so the banding step has to run last.
    ///
    /// UNTESTED: never run against a drawing.
    /// </summary>
    public sealed class FtfPipeline
    {
        private readonly List<FtfStep> _steps = new List<FtfStep>();

        public FtfPipeline()
        {
            var points = new TreeCommands();
            var drip = new DripCommands();
            var labels = new LabelCommands();
            var tags = new TagCommands();
            var order = new DrawOrderCommands();

            _steps.Add(new FtfStep
            {
                Name = "FTFPOINTS",
                Title = "Point Finishing",
                Description = "Read every point, interpret its code, report what cannot be handled",
                Run = points.FtfTrees
            });
            _steps.Add(new FtfStep
            {
                Name = "FTFDRIP",
                Title = "Driplines",
                Description = "Tree driplines, trimmed to their outer envelope",
                Run = drip.FtfDrip
            });
            var linework = new LineworkCommands();
            _steps.Add(new FtfStep
            {
                Name = "FTFLINELABELS",
                Title = "Linework Labels",
                Description = "Optional bulk pass over every recognised line. Off by " +
                              "default (Settings > Line Labels) -- FTFLABELLINE, the " +
                              "click-to-place command, is the normal way to label lines " +
                              "and its labels are never touched here",
                Run = linework.FtfLineLabels
            });
            _steps.Add(new FtfStep
            {
                Name = "FTFLABELS",
                Title = "Labels",
                Description = "Labels, masks and leaders, avoiding existing work",
                Run = labels.FtfLabels
            });
            _steps.Add(new FtfStep
            {
                Name = "FTFTAGS",
                Title = "Tags",
                Description = "Tags, reissuing the same numbers on every run",
                Run = tags.FtfTags
            });
            _steps.Add(new FtfStep
            {
                Name = "FTFTABLE",
                Title = "Table",
                Description = "The schedule, keeping its position",
                Run = tags.FtfTable
            });
            _steps.Add(new FtfStep
            {
                Name = "FTFORDER",
                Title = "Final Draw Order",
                Description = "Draw-order banding. Must run last",
                Run = order.FtfOrder
            });
        }

        /// <summary>Steps in the order they must run.</summary>
        public IList<FtfStep> Steps { get { return _steps.AsReadOnly(); } }

        /// <summary>
        /// Runs the selected steps, in pipeline order regardless of the order they were
        /// selected in.
        /// </summary>
        /// <param name="selected">
        /// Step names to run, or null for all of them.
        /// </param>
        /// <param name="starting">Called before each step. Optional.</param>
        /// <param name="onFailure">
        /// Called when a step throws; return true to carry on. Null means stop, which is
        /// the safe default -- later steps assume the earlier ones ran, and finishing a
        /// pass on a broken foundation produces a drawing that looks finished and is not.
        /// </param>
        public FtfRunResult Run(IEnumerable<string> selected,
                                Action<FtfStep> starting,
                                Func<FtfStep, System.Exception, bool> onFailure)
        {
            var wanted = selected == null
                ? null
                : new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);

            var result = new FtfRunResult();

            foreach (var step in _steps)
            {
                if (wanted != null && !wanted.Contains(step.Name)) continue;

                if (starting != null) starting(step);

                try
                {
                    // Each step opens and commits its own transaction, so one failure
                    // cannot roll back what earlier steps already committed.
                    step.Run();

                    // Commands report failure via FtfSession.LastRunError rather than
                    // throwing -- an unhandled exception inside AutoCAD is worse than
                    // a message. Without this check every step looked successful.
                    if (FtfSession.LastRunError != null)
                        throw new System.InvalidOperationException(FtfSession.LastRunError);

                    result.Outcomes.Add(new FtfStepOutcome { Name = step.Name, Succeeded = true });
                }
                catch (System.Exception ex)
                {
                    result.Outcomes.Add(new FtfStepOutcome
                    {
                        Name = step.Name,
                        Succeeded = false,
                        Error = ex.Message
                    });

                    var carryOn = onFailure != null && onFailure(step, ex);
                    if (!carryOn)
                    {
                        result.StoppedEarly = true;
                        return result;
                    }
                }
            }

            return result;
        }
    }
}
