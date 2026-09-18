using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using FieldCodes.Settings;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Layers for production drafting (dips, easements). Before a layer is created
    /// the drawing and the project's layer mappings are checked; a near-duplicate of
    /// an existing layer is put to the drafter instead of being created silently.
    /// Each decision is made once per command.
    /// </summary>
    internal static class ProductionLayers
    {
        private static Transaction _cacheOwner;
        private static readonly Dictionary<string, ObjectId> Cache =
            new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);

        public static ObjectId Get(Database db, Transaction tr, string configured, FtfSettings settings)
        {
            if (!ReferenceEquals(_cacheOwner, tr))
            {
                _cacheOwner = tr;
                Cache.Clear();
            }

            ObjectId cached;
            if (Cache.TryGetValue(configured ?? string.Empty, out cached) && !cached.IsErased) return cached;

            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            var names = new List<string>();
            foreach (ObjectId id in table)
            {
                var record = tr.GetObject(id, OpenMode.ForRead) as LayerTableRecord;
                if (record != null && !record.IsErased) names.Add(record.Name);
            }

            var resolution = ProductionLayerResolver.Resolve(configured, names,
                settings != null ? settings.General.LayerMappings : null);
            var ed = AcadApp.DocumentManager.MdiActiveDocument != null ? AcadApp.DocumentManager.MdiActiveDocument.Editor : null;
            var layer = resolution.Layer;

            switch (resolution.Decision)
            {
                case LayerDecision.Mapped:
                    Say(ed, "Layer {0}: using the project layer {1} (layer mapping).", configured, layer);
                    break;
                case LayerDecision.SameNameDifferentSpelling:
                    Say(ed, "Layer {0}: using the existing layer {1} (same name, different spelling).", configured, layer);
                    break;
                case LayerDecision.AskAboutSimilar:
                    layer = AskAboutSimilar(ed, resolution);
                    break;
                case LayerDecision.Create:
                    Say(ed, "Layer {0} is not in this drawing and nothing similar is; creating it.", layer);
                    break;
            }

            var result = CadUtil.EnsureLayer(db, tr, layer);
            Cache[configured ?? string.Empty] = result;
            return result;
        }

        private static string AskAboutSimilar(Editor ed, LayerResolution resolution)
        {
            if (ed == null) return resolution.Similar[0];

            ed.WriteMessage("\nLayer {0} is not in this drawing, but similar layer(s) are: {1}",
                            resolution.Layer, string.Join(", ", resolution.Similar.ToArray()));
            ed.WriteMessage("\n  Add a layer mapping in Settings > General to decide this permanently.");

            var options = new PromptKeywordOptions("\n  Use the existing layer or create the new one? [Existing/Create] <Existing>: ", "Existing Create");
            options.Keywords.Default = "Existing";
            options.AllowNone = true;
            var answer = ed.GetKeywords(options);
            if (answer.Status == PromptStatus.OK && answer.StringResult == "Create") return resolution.Layer;

            if (resolution.Similar.Count == 1) return resolution.Similar[0];
            for (var i = 0; i < resolution.Similar.Count; i++)
                ed.WriteMessage("\n  {0}. {1}", i + 1, resolution.Similar[i]);
            var pick = ed.GetInteger(new PromptIntegerOptions("\n  Which layer number <1>: ")
            {
                AllowNone = true, LowerLimit = 1, UpperLimit = resolution.Similar.Count, DefaultValue = 1, UseDefaultValue = true
            });
            return resolution.Similar[pick.Status == PromptStatus.OK ? pick.Value - 1 : 0];
        }

        private static void Say(Editor ed, string format, params object[] args)
        {
            if (ed != null) ed.WriteMessage("\n  " + format, args);
        }
    }
}
