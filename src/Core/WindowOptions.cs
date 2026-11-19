using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AutoDraw.Core
{
    /// <summary>
    /// Options another program passes with the files it opens (Automation.OpenWith): "key=value; key=value" (or one per
    /// line), or a JSON object. Keys are case-insensitive; spaces, '-' and '_' in them are ignored.
    /// </summary>
    internal static class WindowOptions
    {
        public static List<KeyValuePair<string, string>> Parse(string text)
        {
            var list = new List<KeyValuePair<string, string>>();
            text = (text ?? "").Trim();
            if (text.Length == 0) return list;

            if (text.StartsWith("{"))
            {
                using (JsonDocument doc = JsonDocument.Parse(text))
                    foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                        list.Add(new KeyValuePair<string, string>(Key(p.Name), Text(p.Value)));
                return list;
            }

            foreach (string part in text.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                list.Add(new KeyValuePair<string, string>(Key(part.Substring(0, eq)), part.Substring(eq + 1).Trim().Trim('"')));
            }
            return list;
        }

        public static string Key(string k) => new string((k ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '_').ToArray()).ToLowerInvariant();

        private static string Text(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.String: return e.GetString() ?? "";
                case JsonValueKind.True: return "true";
                case JsonValueKind.False: return "false";
                case JsonValueKind.Array: return string.Join("; ", e.EnumerateArray().Select(Text));
                case JsonValueKind.Null: return "";
                default: return e.GetRawText();
            }
        }

        /// <summary>on/off, true/false, yes/no, ja/nee, 1/0; null when it is none of these.</summary>
        public static bool? Bool(string v)
        {
            switch ((v ?? "").Trim().ToLowerInvariant())
            {
                case "on": case "true": case "yes": case "ja": case "1": case "aan": return true;
                case "off": case "false": case "no": case "nee": case "0": case "uit": return false;
                default: return null;
            }
        }

        /// <summary>The document type a key names: enum name, label, short label or a few other words for it.</summary>
        public static DocKind? Kind(string key)
        {
            key = Key(key);
            foreach (DocKind k in DocKinds.All)
                if (key == Key(k.ToString()) || key == Key(DocKinds.Label(k))) return k;
            switch (key)
            {
                case "assemblies": case "asm": return DocKind.Assembly;
                case "bent": case "folded": case "foldedsheet": case "zetwerk": return DocKind.SheetMetalFolded;
                case "flat": case "plate": return DocKind.SheetMetalFlat;
                case "lathepart": case "turned": return DocKind.Lathe;
                case "tube": case "profile": case "tubeprofile": return DocKind.Tube;
                default: return null;
            }
        }
    }
}
