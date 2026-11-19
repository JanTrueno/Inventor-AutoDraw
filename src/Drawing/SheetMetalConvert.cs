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
                double volume = 0, area = 0, faces = 0;
                foreach (SurfaceBody b in def.SurfaceBodies)
                {
                    if (b.Faces.Count == 0) continue;
                    volume += b.Volume[0.001];
                    foreach (Face f in b.Faces) area += f.Evaluator.Area;
                    faces = Thickness.OfFaces(b);
                }
                if (volume <= 0 || area <= 0) return 0;
                double rough = 2 * volume / area;   // a little too thin, but tells a sheet from a block
                // the distance between the sheet's faces, when it agrees with the rough measure
                double t = faces > 0 && faces >= rough * 0.8 && faces <= rough * 1.3 ? faces : Math.Round(rough * 100) / 100.0;
                KindDetector.ShortestAxis(def.RangeBox, out double shortest, out double middle);
                // thin: at most 25 mm, and small next to the part (a block or a bar is not a sheet)
                return t > 0 && t <= 2.5 && t <= 0.25 * middle ? t : 0;
            }
            catch (Exception ex) { Log.Write("Measuring the sheet thickness failed: " + ex.Message); return 0; }
        }

        /// <summary>
        /// An imported part (a STEP or other neutral file): its solid is an imported base body, not modelled with
        /// Inventor features. Only these are measured and converted; parts modelled in Inventor are left as they are.
        /// </summary>
        public static bool IsImported(PartDocument part)
        {
            try
            {
                PartComponentDefinition def = part.ComponentDefinition;
                PartFeatures features = def.Features;
                int imported = 0;
                try { imported += features.NonParametricBaseFeatures.Count; } catch { }
                try { imported += features.ReferenceFeatures.Count; } catch { }
                try { imported += def.ReferenceComponents.ImportedComponents.Count; } catch { }
                return imported > 0;
            }
            catch { return false; }
        }

        public static bool IsSheetMetal(PartDocument part) =>
            string.Equals(part.SubType, KindDetector.SheetMetalSubType, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// A real sheet metal part: sheet metal with bends or a flat pattern. An imported solid can be a sheet metal part
        /// already (Inventor's import can give it that type) while it has neither and has the style's thickness, not its own.
        /// </summary>
        public static bool IsFinishedSheetMetal(PartDocument part)
        {
            if (!IsSheetMetal(part)) return false;
            try
            {
                var sm = (SheetMetalComponentDefinition)part.ComponentDefinition;
                return sm.HasFlatPattern || sm.Bends.Count > 0;
            }
            catch { return true; }
        }

        /// <summary>Converts the part; returns false (and why) when it is not one thin solid or Inventor refuses.</summary>
        public static bool Convert(PartDocument part, out string why)
        {
            why = null;
            if (IsFinishedSheetMetal(part)) return true;
            if (!IsImported(part)) { why = "modelled in Inventor, not imported: make it sheet metal in Inventor"; return false; }
            double t = SheetThickness(part);
            if (t <= 0) { why = "not one solid of one thin thickness"; return false; }
            string mm = (t * 10).ToString("0.#", CultureInfo.InvariantCulture) + " mm";
            try
            {
                if (!IsSheetMetal(part)) part.SubType = KindDetector.SheetMetalSubType;
                var sm = (SheetMetalComponentDefinition)part.ComponentDefinition;
                // the thickness parameter cannot be set on a converted part; a style of that thickness can
                string name = "AutoDraw " + mm;
                double now = -1;
                try { now = System.Convert.ToDouble(sm.Thickness.Value); } catch { }
                if (Math.Abs(now - t) > 0.004)   // a native sheet metal part of the right thickness keeps its style
                {
                    SheetMetalStyle style = null;
                    try { style = (SheetMetalStyle)sm.SheetMetalStyles[name]; } catch { }
                    if (style == null)
                    {
                        style = (SheetMetalStyle)sm.ActiveSheetMetalStyle.Copy(name);
                        style.Thickness = mm;
                    }
                    style.Activate();
                    sm.UseSheetMetalStyleThickness = true;
                }
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
