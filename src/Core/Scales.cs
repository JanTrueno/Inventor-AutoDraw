using System;

namespace AutoDraw.Core
{
    /// <summary>The drawing scales AutoDraw picks from, and how a view is fitted into a region of the sheet.</summary>
    internal static class Scales
    {
        /// <summary>Enlarging and reducing scales, largest first.</summary>
        public static readonly double[] Standard =
        {
            10, 5, 4, 3, 2, 1,
            1 / 1.5, 1 / 2.0, 1 / 2.5, 1 / 3.0, 1 / 4.0, 1 / 5.0, 1 / 6.0, 1 / 8.0, 1 / 10.0, 1 / 12.0, 1 / 15.0,
            1 / 20.0, 1 / 25.0, 1 / 30.0, 1 / 40.0, 1 / 50.0, 1 / 75.0, 1 / 100.0, 1 / 150.0, 1 / 200.0
        };

        /// <summary>The largest standard scale not above <paramref name="max"/> (the smallest one when none fits).</summary>
        public static double Snap(double max)
        {
            if (double.IsNaN(max) || max <= 0) return Standard[Standard.Length - 1];
            foreach (double s in Standard) if (s <= max * 1.0000001) return s;
            return Standard[Standard.Length - 1];
        }

        /// <summary>The scale at which a <paramref name="w"/> × <paramref name="h"/> model fits into a region, with room for dimensions.</summary>
        public static double Fit(double w, double h, double regionW, double regionH, double fill = 0.78)
        {
            double sx = w > 1e-6 ? regionW * fill / w : double.MaxValue;
            double sy = h > 1e-6 ? regionH * fill / h : double.MaxValue;
            double s = Math.Min(sx, sy);
            return s == double.MaxValue ? 1 : s;
        }

        /// <summary>The next standard scale below <paramref name="s"/>.</summary>
        public static double Below(double s)
        {
            foreach (double x in Standard) if (x < s * 0.9999) return x;
            return Standard[Standard.Length - 1];
        }
    }
}
