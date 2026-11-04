using System;
using System.Reflection;

namespace FieldCodes
{
    /// <summary>
    /// Reads a public instance property by name, for the Civil 3D members FTF reads by reflection
    /// (so a member missing from one release means "not read", never a plugin that fails to load).
    ///
    /// <see cref="Type.GetProperty(string, BindingFlags)"/> throws AmbiguousMatchException when a
    /// class redeclares a property its base already has with another type. A caller that catches
    /// that loses the value without knowing why. That is how the accountability smoke test at
    /// 65d1c05 read no point style name and no point label, and reported a symbol and a label as
    /// missing that it had simply not read. This reader takes the most derived declaration, and
    /// says why when it cannot read one.
    /// </summary>
    public static class PropertyReader
    {
        /// <summary>The property of that name, the most derived declaration when several
        /// classes in the hierarchy declare one; null when there is none (or none readable /
        /// writable as asked).</summary>
        public static PropertyInfo Find(Type type, string name, bool writable = false)
        {
            if (type == null || string.IsNullOrEmpty(name)) return null;
            PropertyInfo best = null;
            var bestDepth = -1;
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!string.Equals(p.Name, name, StringComparison.Ordinal) || p.GetIndexParameters().Length != 0) continue;
                if (writable ? !p.CanWrite : !p.CanRead) continue;
                var depth = Depth(p.DeclaringType);
                if (depth > bestDepth) { best = p; bestDepth = depth; }
            }
            return best;
        }

        /// <summary>The value, or false with the reason: no such property, or its getter threw
        /// (the inner exception is named).</summary>
        public static bool TryRead(object target, string name, out object value, out string problem)
        {
            value = null;
            problem = null;
            if (target == null) { problem = name + ": nothing to read it from"; return false; }
            var p = Find(target.GetType(), name);
            if (p == null) { problem = target.GetType().Name + " has no readable " + name; return false; }
            try
            {
                value = p.GetValue(target, null);
                return true;
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException ?? ex;
                problem = target.GetType().Name + "." + name + " threw " + inner.GetType().Name + ": " + inner.Message;
                return false;
            }
            catch (Exception ex)
            {
                problem = target.GetType().Name + "." + name + ": " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>The value, or null when it cannot be read.</summary>
        public static object Read(object target, string name)
        {
            object value;
            string problem;
            return TryRead(target, name, out value, out problem) ? value : null;
        }

        private static int Depth(Type t)
        {
            var d = 0;
            for (var x = t; x != null; x = x.BaseType) d++;
            return d;
        }
    }
}
