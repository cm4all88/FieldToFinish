using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Settings;

namespace FieldCodes.Utilities
{
    /// <summary>What Find all connections worked out for one pipe that had no connection.</summary>
    public sealed class BatchProposal
    {
        public string FromStructureId { get; set; }
        public string PipeId { get; set; }
        /// <summary>The structure it most likely runs to; null when nothing was found.</summary>
        public string ToStructureId { get; set; }
        /// <summary>The pipe observed at that structure pointing back, when there is one.</summary>
        public string MatchingPipeId { get; set; }
        public Confidence Confidence { get; set; }
        public double Distance { get; set; }
        public double DeviationDegrees { get; set; }
        public IList<string> Basis { get; set; }
        /// <summary>True once Find all connections confirmed it itself (high confidence only).</summary>
        public bool AutoConfirmed { get; set; }
        /// <summary>Why there is no proposal, when there is none.</summary>
        public string NothingBecause { get; set; }

        public BatchProposal() { Basis = new List<string>(); }
    }

    /// <summary>
    /// Find all connections: the connection search run for every pipe that is not connected yet, then paired across
    /// the whole project so no observed pipe is claimed by two structures, and the two ends of one pipe become one
    /// connection. It uses the same search and scoring as finding one pipe's connections; nothing about how a
    /// candidate is scored is changed.
    /// </summary>
    public static class ConnectionBatch
    {
        public const string AutoBasis = "Confirmed automatically by Find all connections: a matching pipe was observed at both ends, size and material agree and both directions line up";

        /// <summary>The pairing, strongest first. Nothing is changed in the project.</summary>
        public static IList<BatchProposal> FindAll(UtilityProject project, UtilitySettings settings)
        {
            settings = settings ?? new UtilitySettings();
            var open = new List<Tuple<StructureRecord, PipeObservation>>();
            foreach (var s in project.Structures)
                foreach (var p in s.Field.Pipes)
                    if (project.ConnectionFor(s.Id, p.Id) == null) open.Add(Tuple.Create(s, p));

            // Every (pipe, candidate) pair the search offers, strongest first.
            var pairs = new List<Tuple<StructureRecord, PipeObservation, ConnectionCandidate>>();
            foreach (var o in open)
                foreach (var c in ConnectionFinder.Find(project, o.Item1, o.Item2, settings))
                    pairs.Add(Tuple.Create(o.Item1, o.Item2, c));
            pairs = pairs.OrderByDescending(x => x.Item3.Score)
                         .ThenBy(x => x.Item3.Distance)
                         .ThenBy(x => x.Item1.Label, StringComparer.Ordinal)
                         .ToList();

            // Greedy, strongest first: a pipe takes one structure; a pipe observed at the far end serves one pipe; the
            // far pipe of an accepted pair is its other end and gets no proposal of its own.
            var claimed = new HashSet<string>();
            var proposals = new List<BatchProposal>();
            // A pipe whose best structure's back pipe went to another pipe: it may still run there (a pipe not dipped).
            var bumped = new Dictionary<string, Tuple<StructureRecord, PipeObservation, ConnectionCandidate>>();
            foreach (var x in pairs)
            {
                var key = Key(x.Item1.Id, x.Item2.Id);
                if (claimed.Contains(key)) continue;
                var match = x.Item3.MatchingPipe;
                string matchKey = match == null ? null : Key(x.Item3.Structure.Id, match.Id);
                if (matchKey != null && claimed.Contains(matchKey))
                {
                    if (!bumped.ContainsKey(key)) bumped[key] = x;
                    continue;
                }
                claimed.Add(key);
                if (matchKey != null) claimed.Add(matchKey);
                proposals.Add(new BatchProposal
                {
                    FromStructureId = x.Item1.Id,
                    PipeId = x.Item2.Id,
                    ToStructureId = x.Item3.Structure.Id,
                    MatchingPipeId = match != null ? match.Id : null,
                    Confidence = x.Item3.Confidence,
                    Distance = x.Item3.Distance,
                    DeviationDegrees = x.Item3.DeviationDegrees,
                    Basis = x.Item3.Basis.ToList()
                });
            }

            foreach (var b in bumped.Where(b => !claimed.Contains(b.Key)))
            {
                var x = b.Value;
                claimed.Add(b.Key);
                var basis = x.Item3.Basis.Where(l => !l.StartsWith("Matching field observation", StringComparison.Ordinal)).ToList();
                basis.Add("The pipe observed at " + x.Item3.Structure.Label + " pointing back is paired with another pipe; none is observed there for this one");
                proposals.Add(new BatchProposal
                {
                    FromStructureId = x.Item1.Id, PipeId = x.Item2.Id, ToStructureId = x.Item3.Structure.Id,
                    Confidence = Confidence.Low, Distance = x.Item3.Distance, DeviationDegrees = x.Item3.DeviationDegrees, Basis = basis
                });
            }

            // Pipes left over: say why.
            foreach (var o in open.Where(o => !claimed.Contains(Key(o.Item1.Id, o.Item2.Id))))
                proposals.Add(new BatchProposal
                {
                    FromStructureId = o.Item1.Id,
                    PipeId = o.Item2.Id,
                    Confidence = Confidence.None,
                    NothingBecause = !o.Item2.Direction.IsKnown ? "no direction recorded, so there is nothing to search along"
                        : o.Item1.Cad == null ? "the structure has no survey point"
                        : "no surveyed structure within " + settings.SearchDistanceFt.ToString("0", System.Globalization.CultureInfo.InvariantCulture) +
                          "' in its direction that is not already taken"
                });

            return proposals.OrderBy(p => Rank(p.Confidence)).ToList();
        }

        /// <summary>
        /// Confirms the high-confidence proposals, exactly as confirming a searched candidate does, and says on each
        /// connection that Find all connections confirmed it. Returns how many were confirmed.
        /// </summary>
        public static int ConfirmHigh(UtilityProject project, IList<BatchProposal> proposals, UtilitySettings settings)
        {
            var confirmed = 0;
            foreach (var p in proposals.Where(p => p.Confidence == Confidence.High && p.ToStructureId != null))
            {
                var from = project.Structure(p.FromStructureId);
                var pipe = project.Pipe(p.FromStructureId, p.PipeId);
                var to = project.Structure(p.ToStructureId);
                if (from == null || pipe == null || to == null || project.ConnectionFor(from.Id, pipe.Id) != null) continue;
                var candidate = ConnectionFinder.Find(project, from, pipe, settings).FirstOrDefault(c => c.Structure.Id == to.Id);
                // The drawing changed since, or the far pipe is not the one the pairing chose: left for the drafter.
                if (candidate == null || candidate.Confidence != Confidence.High ||
                    (candidate.MatchingPipe == null ? null : candidate.MatchingPipe.Id) != p.MatchingPipeId) continue;
                var connection = ConnectionFinder.Accept(project, from, pipe, candidate, false, null);
                connection.Basis.Add(AutoBasis);
                var entry = project.Overrides.LastOrDefault(o => o.Target == connection.Id);
                if (entry != null) entry.What = "Confirmed by Find all connections";
                p.AutoConfirmed = true;
                confirmed++;
            }
            return confirmed;
        }

        private static string Key(string structureId, string pipeId) { return structureId + "/" + pipeId; }

        private static int Rank(Confidence c)
        {
            switch (c)
            {
                case Confidence.High: return 0;
                case Confidence.Medium: return 1;
                case Confidence.Low: return 2;
                default: return 3;
            }
        }
    }
}
