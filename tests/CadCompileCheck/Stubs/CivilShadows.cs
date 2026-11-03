// Civil 3D type names that AutoCAD or .NET namespaces also declare. The plugin does not use
// these types; they are here so a bare use of the name, in a file that imports the Civil
// namespace beside the other one, is ambiguous (CS0104) in the compile checks as it is in the
// real Windows build against AeccDbMgd.dll.
//
// Civil.cs declares only the Civil types the plugin uses. That is how DBObject in
// Accountability.cs (Autodesk.Civil.DatabaseServices.DBObject and
// Autodesk.AutoCAD.DatabaseServices.DBObject) passed every check here and stopped UPDATEFTF at
// fb3d1b2.
//
// Only names confirmed in Civil 3D 2024's AeccDbMgd belong here. For every other Civil name,
// tools/CivilApiNames writes CivilApiNames.g.cs from the installed assemblies (see its README).
// AmbiguousTypeNameTests checks that each Civil row of its table has a declaration here or in
// Civil.cs.
using System;

namespace Autodesk.Civil.DatabaseServices
{
    /// <summary>Civil 3D's base of its non-graphical objects (styles, settings).</summary>
    public abstract class DBObject : Autodesk.AutoCAD.DatabaseServices.DBObject
    {
        protected internal DBObject(IntPtr unmanagedPointer, bool autoDelete) : base(unmanagedPointer, autoDelete) { }
    }

    /// <summary>Civil 3D's base of its entities (COGO points, labels, surfaces, pipes).</summary>
    public abstract class Entity : Autodesk.AutoCAD.DatabaseServices.Entity
    {
        protected internal Entity(IntPtr unmanagedPointer, bool autoDelete) : base(unmanagedPointer, autoDelete) { }
    }

    /// <summary>A sample-line section. AutoCAD's Section is a section plane.</summary>
    public sealed class Section : Entity
    {
        private Section() : base(IntPtr.Zero, false) { }
    }

    /// <summary>A corridor assembly. System.Reflection declares Assembly too.</summary>
    public sealed class Assembly : Entity
    {
        private Assembly() : base(IntPtr.Zero, false) { }
    }
}
