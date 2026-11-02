using System;

namespace FieldCodes.Stamps
{
    /// <summary>
    /// Which entity an FTF stamp was written on. Every stamp records the handle of the entity
    /// it was written on; AutoCAD gives every copy a new handle but carries the XData across,
    /// so a label someone COPYed, pasted or arrayed still carries FTF's stamp with the original's
    /// handle. Such a copy is the user's work: FTF never re-places, replaces or erases it.
    /// Stamps written before this field existed have no handle and are still FTF's.
    /// </summary>
    public static class StampIdentity
    {
        /// <summary>True when the stamp was written on another entity: this one is a copy.</summary>
        public static bool IsCopy(string stampedHandle, string actualHandle)
        {
            if (string.IsNullOrWhiteSpace(stampedHandle)) return false;          // older stamp: FTF's
            if (string.IsNullOrWhiteSpace(actualHandle)) return true;            // not in a drawing yet
            return !string.Equals(stampedHandle.Trim(), actualHandle.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
