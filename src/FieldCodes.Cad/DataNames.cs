namespace FieldCodes.Cad
{
    /// <summary>
    /// Names of the data the plugin stores inside drawings. The commercial edition uses its own
    /// so it never reads or erases what the internal edition made, and the other way round.
    /// The internal edition keeps the original name exactly.
    /// </summary>
    internal static class DataNames
    {
#if TRACE_EDITION
        public const string Root = "TRACE_OBJECTS";
#else
        public const string Root = "FTF_FIELDTOFINISH";
#endif
    }
}
