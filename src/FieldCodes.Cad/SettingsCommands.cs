using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Cad.Setup;

namespace FieldCodes.Cad
{
    /// <summary>
    /// FTFSETUP and its aliases now land on the Settings page of the main window, so
    /// configuration is one experience whether it is reached by command or by
    /// navigation. The old standalone setup dialog is gone; its section pages live on
    /// inside the window.
    /// </summary>
    public sealed class SettingsCommands
    {
        [CommandMethod("FTFSETUP", CommandFlags.Modal)]
        [CommandMethod("FTFSETTINGS", CommandFlags.Modal)]
        [CommandMethod("FTFCONFIG", CommandFlags.Modal)]
        [CommandMethod("FTFOPTIONS", CommandFlags.Modal)]
        public void FtfSetup()
        {
            FtfCommand.ShowWindow("Settings");
        }

        /// <summary>
        /// Everything the settings pages need: the drawing's named resources, the
        /// rules, and the resolved settings. Needs an open transaction.
        /// </summary>
        internal static SetupContext BuildContext(Database db, Transaction tr)
        {
            var context = new SetupContext
            {
                Drawing = new DrawingResources(db, tr),
                RulesResolution = FtfSession.ResolveRules(db)
            };

            try
            {
                context.Rules = FtfSession.Rules(db);
                context.RulesPath = context.RulesResolution.ActivePath;
            }
            catch (ConfigException ex)
            {
                context.Rules = null;
                context.RulesPath = context.RulesResolution.ActivePath;
                context.RulesError = ex.Message;
            }

            context.Resolution = FtfSession.ResolveSettings(db, context.Rules);
            return context;
        }
    }
}
