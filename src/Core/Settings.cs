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

        public string OrderProperty { get; set; } = "ordernummer";
        public string OrderPlaceholder { get; set; } = "160100";
        public int OrderMinDigits { get; set; } = 6;

        public string QuantityProperty { get; set; } = "Aantal";
        public List<string> SheetNumberProperties { get; set; } = new List<string> { "bewerking", "bewerkingBlad" };

        /// <summary>An assembly with more parts list rows than this gets two sheets.</summary>
        public int LargeAssemblyRows { get; set; } = 15;

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
