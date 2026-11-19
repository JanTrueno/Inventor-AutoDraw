using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AutoDraw.Core
{
    /// <summary>Settings of the current Windows user, in %APPDATA%\AutoDraw\settings.json.</summary>
    internal sealed class AppSettings
    {
        public string TemplatePath { get; set; } = "";
        public List<string> ExtraColumns { get; set; } = new List<string>();
        /// <summary>The tree shows the Location column (folder of each file).</summary>
        public bool ShowLocation { get; set; } = true;

        public string OrderProperty { get; set; } = "ordernummer";
        public string OrderPlaceholder { get; set; } = "160100";
        public int OrderMinDigits { get; set; } = 6;

        public string QuantityProperty { get; set; } = "Aantal";
        public List<string> SheetNumberProperties { get; set; } = new List<string> { "bewerking", "bewerkingBlad" };

        /// <summary>The model's up axis, "Z" (models standing on the XY plane) or "Y" (Inventor's default).</summary>
        public string UpAxis { get; set; } = "Z";
        [System.Text.Json.Serialization.JsonIgnore]
        public bool ZUp => !string.Equals(UpAxis, "Y", StringComparison.OrdinalIgnoreCase);

        /// <summary>The iProperty of the new drawing that gets the order number (the title block shows it); empty = none.</summary>
        public string DrawingOrderProperty { get; set; } = "ordernummer";

        /// <summary>The iProperty a sheet is named after (e.g. Part Number); empty, or empty in the document: the file name.</summary>
        public string SheetNameProperty { get; set; } = "Part Number";

        /// <summary>External iLogic rules (names as in the iLogic browser) run on the new drawing before it is saved.</summary>
        public List<string> DrawingRules { get; set; } = new List<string>();

        /// <summary>Kinds (<see cref="DocKind"/> names) that start unticked: no sheet unless ticked by hand.</summary>
        public List<string> SkippedKinds { get; set; } = new List<string>();
        public bool Skips(DocKind kind) => SkippedKinds.Exists(k => string.Equals(k, kind.ToString(), StringComparison.OrdinalIgnoreCase));

        /// <summary>An assembly with more parts list rows than this gets two sheets.</summary>
        public int LargeAssemblyRows { get; set; } = 15;

        // ----- bulk part numbers (Numbering… window) -----

        /// <summary>Tokens: {order} (last digits of the order number), {asm}, {seq}, {type}, {name} (file name).</summary>
        public string NumberPattern { get; set; } = "{order}.{asm}.{seq}-{type}";
        /// <summary>How many of the order number's last digits {order} holds; 0 = all.</summary>
        public int NumberOrderDigits { get; set; } = 4;
        /// <summary>The iProperties that get the number: built-in (by name, e.g. Part Number) or custom (added when missing).</summary>
        public List<string> NumberProperties { get; set; } = new List<string> { "Part Number" };
        /// <summary>Type letter per <see cref="DocKind"/> name; missing kinds use the default letters (S Z P D B G).</summary>
        public Dictionary<string, string> TypeLetters { get; set; } = new Dictionary<string, string>();
        /// <summary>Documents whose number already fits the pattern keep their assembly code and sequence number.</summary>
        public bool NumberKeepExisting { get; set; } = true;
        /// <summary>Assembly levels with a code of their own (3: 100/110/111, 4: 1000/1100/1110/1111); {asm} has as many digits.</summary>
        public int NumberLevels { get; set; } = 3;
        /// <summary>Unticked documents that carry a number of this order get their file name back (Inventor's default part number).</summary>
        public bool NumberResetSkipped { get; set; }
        /// <summary>The Settings part of the Numbering window is unfolded.</summary>
        public bool NumberSettingsOpen { get; set; }
        public int NumberWindowW { get; set; } = 960;
        public int NumberWindowH { get; set; } = 680;

        /// <summary>The Settings part of the window is unfolded.</summary>
        public bool SettingsOpen { get; set; }

        /// <summary>Before writing: look for checked-in (read-only) files and ask whether to check them out in Vault.</summary>
        public bool AskCheckOut { get; set; } = true;

        /// <summary>Save the drawing when it is made or updated. Off: it stays open unsaved, with its file name set, for the user to save.</summary>
        public bool SaveDrawing { get; set; }

        public int WindowX { get; set; } = -1;
        public int WindowY { get; set; } = -1;
        public int WindowW { get; set; } = 1280;
        public int WindowH { get; set; } = 760;
        public bool WindowMaximized { get; set; }

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };

        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoDraw", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
            }
            catch (Exception ex) { Log.Write("Reading " + FilePath + " failed: " + ex.Message); }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
            }
            catch (Exception ex) { Log.Write("Writing " + FilePath + " failed: " + ex.Message); }
        }
    }
}
