using System;
using System.Collections.Generic;
using Inventor;

namespace AutoDraw.Core
{
    /// <summary>Reading and writing iProperties by name (display name or internal name, any property set).</summary>
    internal static class Props
    {
        public const string CustomSet = "Inventor User Defined Properties";

        public static string Get(Document doc, string name)
        {
            if (doc == null || string.IsNullOrWhiteSpace(name)) return "";
            try
            {
                foreach (PropertySet set in doc.PropertySets)
                {
                    foreach (Property p in set)
                    {
                        if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(p.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                            return Convert.ToString(p.Value) ?? "";
                    }
                }
            }
            catch { }
            return "";
        }

        /// <summary>
        /// Sets a property: a built-in one when a property of that name exists, else a custom one (added when missing).
        /// Returns true when the value changed.
        /// </summary>
        public static bool Set(Document doc, string name, string value)
        {
            value = value ?? "";
            foreach (PropertySet set in doc.PropertySets)
            {
                foreach (Property p in set)
                {
                    if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        if ((Convert.ToString(p.Value) ?? "") == value) return false;
                        p.Value = value;
                        return true;
                    }
                }
            }
            doc.PropertySets[CustomSet].Add(value, name);
            return true;
        }

        public static IEnumerable<string> CustomNames(Document doc)
        {
            var names = new List<string>();
            try { foreach (Property p in doc.PropertySets[CustomSet]) names.Add(p.Name); } catch { }
            return names;
        }

        public static string Material(Document doc)
        {
            try
            {
                if (doc is PartDocument part) return part.ComponentDefinition.Material?.Name ?? "";
            }
            catch { }
            return Get(doc, "Material");
        }
    }
}
