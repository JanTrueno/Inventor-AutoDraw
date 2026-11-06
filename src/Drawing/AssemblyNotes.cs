using System;
using System.Collections.Generic;
using System.Linq;
using AutoDraw.Core;
using Inventor;

namespace AutoDraw.Drawing
{
    /// <summary>The parts list of an assembly sheet (bottom right, on top of the title block) and its balloons.</summary>
    internal static class AssemblyNotes
    {
        /// <summary>Adds the structured (first level) parts list and moves it onto the title block's top edge.</summary>
        public static PartsList AddPartsList(Inventor.Application app, Sheet sheet, DrawingView view, SheetFrame frame)
        {
            TransientGeometry tg = app.TransientGeometry;
            PartsList list = sheet.PartsLists.Add(view, tg.CreatePoint2d(frame.TitleBlock.X1, frame.TitleBlock.Y1 + 10),
                PartsListLevelEnum.kStructured, Type.Missing, 1, false);
            try
            {
                Box2d rb = list.RangeBox;
                double dx = frame.TitleBlock.X1 - rb.MaxPoint.X, dy = frame.TitleBlock.Y1 - rb.MinPoint.Y;
                Point2d p = list.Position;
                list.Position = tg.CreatePoint2d(p.X + dx, p.Y + dy);
            }
            catch (Exception ex) { Log.Write("Moving the parts list failed: " + ex.Message); }
            return list;
        }

        /// <summary>The top of the parts list, or the top of the title block when there is none.</summary>
        public static double Top(PartsList list, SheetFrame frame)
        {
            try { return list.RangeBox.MaxPoint.Y; } catch { return frame.TitleBlock.Y1; }
        }

        /// <summary>
        /// One balloon per parts list row: the leader starts on an edge of the first occurrence of that component in the
        /// view, the balloon sits just outside the view, spread around it.
        /// </summary>
        public static void AddBalloons(Inventor.Application app, Sheet sheet, DrawingView view, AssemblyDocument asm)
        {
            TransientGeometry tg = app.TransientGeometry;
            double cx = view.Center.X, cy = view.Center.Y, hw = view.Width / 2 + 1.5, hh = view.Height / 2 + 1.5;

            var targets = new List<(GeometryIntent Intent, double X, double Y)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ComponentOccurrence occ in RowOccurrences(asm.ComponentDefinition.Occurrences))
            {
                try
                {
                    string key = (occ.ReferencedDocumentDescriptor?.ReferencedDocument as Document)?.FullDocumentName;
                    if (key == null || !seen.Add(key)) continue;
                    (GeometryIntent intent, double x, double y)? t = Anchor(sheet, view, occ);
                    if (t != null) targets.Add(t.Value);
                }
                catch (Exception ex) { Log.Write("Balloon anchor failed: " + ex.Message); }
            }
            if (targets.Count == 0) return;

            // a position on the rectangle around the view for each target, in the direction of the target from the centre
            var placed = targets.Select(t =>
            {
                double dx = t.X - cx, dy = t.Y - cy;
                if (Math.Abs(dx) < 1e-6 && Math.Abs(dy) < 1e-6) dy = 1;
                double k = Math.Min(Math.Abs(dx) > 1e-9 ? hw / Math.Abs(dx) : double.MaxValue, Math.Abs(dy) > 1e-9 ? hh / Math.Abs(dy) : double.MaxValue);
                return (t.Intent, X: cx + dx * k, Y: cy + dy * k, Angle: Math.Atan2(dy, dx));
            }).OrderBy(p => p.Angle).ToList();

            // keep balloons apart: walk around and push each one along the rectangle when it is too close to the previous one
            const double minGap = 1.4;
            for (int i = 1; i < placed.Count; i++)
            {
                var a = placed[i - 1];
                var b = placed[i];
                double d = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
                if (d >= minGap) continue;
                double angle = b.Angle + 0.08;
                for (int tries = 0; tries < 40 && d < minGap; tries++, angle += 0.06)
                {
                    double dx = Math.Cos(angle), dy = Math.Sin(angle);
                    double k = Math.Min(Math.Abs(dx) > 1e-9 ? hw / Math.Abs(dx) : double.MaxValue, Math.Abs(dy) > 1e-9 ? hh / Math.Abs(dy) : double.MaxValue);
                    b = (b.Intent, cx + dx * k, cy + dy * k, angle);
                    d = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
                }
                placed[i] = b;
            }

            foreach (var p in placed)
            {
                try
                {
                    ObjectCollection leader = app.TransientObjects.CreateObjectCollection(Type.Missing);
                    leader.Add(tg.CreatePoint2d(p.X, p.Y));
                    leader.Add(p.Intent);
                    sheet.Balloons.Add(leader, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
                }
                catch (Exception ex) { Log.Write("Balloon failed: " + ex.Message); }
            }
        }

        /// <summary>The occurrences that are rows of the structured parts list (children of phantoms count as rows).</summary>
        private static IEnumerable<ComponentOccurrence> RowOccurrences(System.Collections.IEnumerable occurrences)
        {
            foreach (ComponentOccurrence occ in occurrences)
            {
                bool skip = false, phantom = false;
                try
                {
                    skip = occ.Suppressed || !occ.Visible;
                    BOMStructureEnum bom = occ.BOMStructure;
                    skip |= bom == BOMStructureEnum.kReferenceBOMStructure;
                    phantom = bom == BOMStructureEnum.kPhantomBOMStructure;
                }
                catch { }
                if (skip) continue;
                if (phantom)
                {
                    foreach (ComponentOccurrence sub in RowOccurrences(occ.SubOccurrences)) yield return sub;
                    continue;
                }
                yield return occ;
            }
        }

        /// <summary>A visible edge of the occurrence in the view: the middle of its longest straight edge, else a circle.</summary>
        private static (GeometryIntent, double, double)? Anchor(Sheet sheet, DrawingView view, ComponentOccurrence occ)
        {
            DrawingCurvesEnumerator curves;
            try { curves = view.get_DrawingCurves(occ); } catch { return null; }
            if (curves == null || curves.Count == 0) return null;

            DrawingCurve best = null;
            double bestLen = 0;
            foreach (DrawingCurve c in curves)
            {
                try
                {
                    Curve2dTypeEnum t = c.ProjectedCurveType;
                    if (t != Curve2dTypeEnum.kLineSegmentCurve2d && t != Curve2dTypeEnum.kLineCurve2d) continue;
                    Point2d s = c.StartPoint, e = c.EndPoint;
                    double len = Math.Sqrt((e.X - s.X) * (e.X - s.X) + (e.Y - s.Y) * (e.Y - s.Y));
                    if (len > bestLen) { bestLen = len; best = c; }
                }
                catch { }
            }
            if (best != null)
            {
                Point2d s = best.StartPoint, e = best.EndPoint;
                return (sheet.CreateGeometryIntent(best, PointIntentEnum.kMidPointIntent), (s.X + e.X) / 2, (s.Y + e.Y) / 2);
            }
            foreach (DrawingCurve c in curves)
            {
                try
                {
                    if (c.ProjectedCurveType != Curve2dTypeEnum.kCircleCurve2d && c.ProjectedCurveType != Curve2dTypeEnum.kCircularArcCurve2d) continue;
                    Box2d rb = c.Evaluator2D.RangeBox;
                    return (sheet.CreateGeometryIntent(c, PointIntentEnum.kCircularTopPointIntent), (rb.MinPoint.X + rb.MaxPoint.X) / 2, rb.MaxPoint.Y);
                }
                catch { }
            }
            return null;
        }
    }
}
