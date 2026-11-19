using System;
using System.Globalization;
using AutoDraw.Core;
using Inventor;

namespace AutoDraw.Drawing
{
    /// <summary>
    /// Makes sheet metal of a plain solid part of one thickness (an imported STEP of a sheet metal part): the thickness
    /// is measured (twice the volume over the area, to 0.1 mm), the part becomes a sheet metal part with a copy of its
    /// sheet metal style of that thickness, and Inventor then finds the bends when the flat pattern is made.
    /// </summary>
    internal static class SheetMetalConvert
    {
        /// <summary>The measured sheet thickness in cm, or 0 when the part is not one thin solid.</summary>
        public static double SheetThickness(PartDocument part)
        {
            try
            {
                PartComponentDefinition def = part.ComponentDefinition;
                if (KindDetector.SolidCount(def) != 1) return 0;
                double volume = 0, area = 0;
                foreach (SurfaceBody b in def.SurfaceBodies)
                {
                    if (b.Faces.Count == 0) continue;
                    volume += b.Volume[0.001];
                    foreach (Face f in b.Faces) area += f.Evaluator.Area;
                }
                if (volume <= 0 || area <= 0) return 0;
                double t = Math.Round(2 * volume / area * 100) / 100.0;   // cm, to 0.1 mm
                KindDetector.ShortestAxis(def.RangeBox, out double shortest, out double middle);
                // thin: at most 25 mm, and small next to the part (a block or a bar is not a sheet)
                return t > 0 && t <= 2.5 && t <= 0.25 * middle ? t : 0;
            }
            catch (Exception ex) { Log.Write("Measuring the sheet thickness failed: " + ex.Message); return 0; }
        }

        public static bool IsSheetMetal(PartDocument part) =>
            string.Equals(part.SubType, KindDetector.SheetMetalSubType, StringComparison.OrdinalIgnoreCase);

        /// <summary>Converts the part; returns false (and why) when it is not one thin solid or Inventor refuses.</summary>
        public static bool Convert(PartDocument part, out string why)
        {
            why = null;
            if (IsSheetMetal(part)) return true;
            double t = SheetThickness(part);
            if (t <= 0) { why = "not one solid of one thin thickness"; return false; }
            string mm = (t * 10).ToString("0.#", CultureInfo.InvariantCulture) + " mm";
            try
            {
                part.SubType = KindDetector.SheetMetalSubType;
                var sm = (SheetMetalComponentDefinition)part.ComponentDefinition;
                // the thickness parameter cannot be set on a converted part; a style of that thickness can
                string name = "AutoDraw " + mm;
                SheetMetalStyle style = null;
                try { style = (SheetMetalStyle)sm.SheetMetalStyles[name]; } catch { }
                if (style == null)
                {
                    style = (SheetMetalStyle)sm.ActiveSheetMetalStyle.Copy(name);
                    style.Thickness = mm;
                }
                style.Activate();
                sm.UseSheetMetalStyleThickness = true;
                try { ((Document)part).Update2(true); } catch { }
                // Inventor finds the bends when it unfolds: make the flat pattern now, so the type is seen right away
                try
                {
                    if (!sm.HasFlatPattern) sm.Unfold();
                    try { sm.FlatPattern.ExitEdit(); } catch { }
                }
                catch (Exception ex) { Log.Write("Unfolding the converted " + ((Document)part).DisplayName + " failed: " + ex.Message); }
                int bends = 0;
                try { bends = sm.Bends.Count; } catch { }
                Log.Write("Converted " + ((Document)part).DisplayName + " to sheet metal, " + mm + ", " + bends + " bends");
                return true;
            }
            catch (Exception ex)
            {
                why = ex.Message;
                Log.Write("Converting " + ((Document)part).DisplayName + " to sheet metal failed: " + ex);
                return false;
            }
        }
    }
}
