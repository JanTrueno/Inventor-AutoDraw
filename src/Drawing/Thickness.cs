using System;
using System.Collections.Generic;
using System.Linq;
using Inventor;

namespace AutoDraw.Drawing
{
    /// <summary>
    /// The sheet thickness of a solid: the distance between the large flat faces and the faces parallel to them on the
    /// other side of the sheet, as most of the sheet's area shows it. Twice the volume over the area comes out too thin
    /// on thick, narrow parts (the edge faces count as area), and with a thickness 0.2 mm off Inventor finds no bends.
    /// </summary>
    internal static class Thickness
    {
        /// <summary>The thickness in cm (to 0.1 mm), or 0 when the solid has no pair of parallel flat faces.</summary>
        public static double OfFaces(SurfaceBody body)
        {
            var planes = new List<(double[] P, double[] N, double Area)>();
            foreach (Face f in body.Faces)
            {
                try
                {
                    if (f.SurfaceType != SurfaceTypeEnum.kPlaneSurface) continue;
                    var pl = (Plane)f.Geometry;
                    double[] p = { pl.RootPoint.X, pl.RootPoint.Y, pl.RootPoint.Z };
                    double[] n = { pl.Normal.X, pl.Normal.Y, pl.Normal.Z };
                    if (f.IsParamReversed) n = n.Select(x => -x).ToArray();
                    planes.Add((p, n, f.Evaluator.Area));
                }
                catch { }
            }
            // per distance (to 0.01 mm), the area of the faces that have an opposite face at that distance
            var votes = new Dictionary<long, double>();
            foreach (var a in planes)
            {
                double best = double.MaxValue;
                foreach (var b in planes)
                {
                    double dot = a.N[0] * b.N[0] + a.N[1] * b.N[1] + a.N[2] * b.N[2];
                    if (dot > -0.9999) continue;   // not facing the other way
                    double d = Math.Abs((b.P[0] - a.P[0]) * a.N[0] + (b.P[1] - a.P[1]) * a.N[1] + (b.P[2] - a.P[2]) * a.N[2]);
                    if (d > 1e-4 && d < best) best = d;
                }
                if (best > 2.5) continue;   // no sheet: more than 25 mm
                long key = (long)Math.Round(best * 1000);
                votes[key] = (votes.TryGetValue(key, out double v) ? v : 0) + a.Area;
            }
            if (votes.Count == 0) return 0;
            long top = votes.OrderByDescending(kv => kv.Value).First().Key;
            return Math.Round(top / 1000.0 * 100) / 100.0;   // cm, to 0.1 mm
        }
    }
}
