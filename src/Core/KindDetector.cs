using System;
using System.Collections.Generic;
using Inventor;

namespace AutoDraw.Core
{
    /// <summary>Which axis of the part's own coordinate system a tube or lathe part runs along.</summary>
    internal enum ModelAxis { None, X, Y, Z }

    /// <summary>
    /// Decides the kind of a part: sheet metal (folded or flat, from its bends), tube (Frame Generator member, or a
    /// long part whose faces all run along its length), lathe part (every face rotational around one axis), else general.
    /// </summary>
    internal static class KindDetector
    {
        public const string SheetMetalSubType = "{9C464203-9BAE-11D3-8BAD-0060B0CE6BB4}";
        private const int MaxFaces = 4000;

        public static DocKind Detect(Document doc)
        {
            if (!(doc is PartDocument part)) return DocKind.General;

            if (string.Equals(part.SubType, SheetMetalSubType, StringComparison.OrdinalIgnoreCase) &&
                part.ComponentDefinition is SheetMetalComponentDefinition sm)
            {
                int bends = 0;
                try { bends = sm.Bends.Count; } catch { }
                return bends > 0 ? DocKind.SheetMetalFolded : DocKind.SheetMetalFlat;
            }

            if (IsFrameMember(doc)) return DocKind.Tube;

            Shape shape = Analyse(part.ComponentDefinition);
            if (shape.Rotational && shape.Long && shape.AlongFraction > 0.85) return DocKind.Tube;   // a pipe
            if (shape.Rotational) return DocKind.Lathe;
            if (shape.Long && shape.AlongFraction > 0.85) return DocKind.Tube;
            return DocKind.General;
        }

        /// <summary>Frame Generator stores its data in attribute sets on the member part.</summary>
        public static bool IsFrameMember(Document doc)
        {
            try
            {
                foreach (AttributeSet set in doc.AttributeSets)
                {
                    string n = set.Name ?? "";
                    if (n.IndexOf("com.autodesk.FG", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("FrameGenerator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("FrameMember", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { }
            return false;
        }

        internal struct Shape
        {
            public bool Rotational;
            public bool Long;             // the longest side is at least 3 times the next one
            public double AlongFraction;  // face area running along the longest side, 0..1
            public ModelAxis LongAxis;
            public ModelAxis RotationAxis;
        }

        /// <summary>The model axis of the longest side of the range box.</summary>
        public static ModelAxis LongestAxis(Box box, out double longest, out double second)
        {
            double dx = box.MaxPoint.X - box.MinPoint.X, dy = box.MaxPoint.Y - box.MinPoint.Y, dz = box.MaxPoint.Z - box.MinPoint.Z;
            var sides = new List<(double L, ModelAxis A)> { (dx, ModelAxis.X), (dy, ModelAxis.Y), (dz, ModelAxis.Z) };
            sides.Sort((a, b) => b.L.CompareTo(a.L));
            longest = sides[0].L;
            second = sides[1].L;
            return sides[0].A;
        }

        public static Shape Analyse(PartComponentDefinition def)
        {
            var shape = new Shape();
            Box box = def.RangeBox;
            shape.LongAxis = LongestAxis(box, out double longest, out double second);
            shape.Long = second > 1e-6 && longest / second >= 3.0;
            (double ax, double ay, double az) = Unit(shape.LongAxis);

            var faces = new List<Face>();
            foreach (SurfaceBody body in def.SurfaceBodies)
            {
                foreach (Face f in body.Faces)
                {
                    faces.Add(f);
                    if (faces.Count > MaxFaces) return shape;
                }
            }
            if (faces.Count == 0) return shape;

            // area running along the longest side: planes parallel to it, cylinders around it
            double total = 0, along = 0;
            var areas = new double[faces.Count];
            for (int i = 0; i < faces.Count; i++)
            {
                double a = 0;
                try { a = faces[i].Evaluator.Area; } catch { }
                areas[i] = a;
                total += a;
                try
                {
                    switch (faces[i].SurfaceType)
                    {
                        case SurfaceTypeEnum.kPlaneSurface:
                            UnitVector n = ((Plane)faces[i].Geometry).Normal;
                            if (Math.Abs(n.X * ax + n.Y * ay + n.Z * az) < 0.01) along += a;
                            break;
                        case SurfaceTypeEnum.kCylinderSurface:
                            UnitVector c = ((Cylinder)faces[i].Geometry).AxisVector;
                            if (Math.Abs(c.X * ax + c.Y * ay + c.Z * az) > 0.999) along += a;
                            break;
                    }
                }
                catch { }
            }
            if (total <= 0) return shape;
            shape.AlongFraction = along / total;

            // rotational: the biggest cylinder or cone gives the axis; every face must turn around it
            int best = -1;
            for (int i = 0; i < faces.Count; i++)
            {
                SurfaceTypeEnum t;
                try { t = faces[i].SurfaceType; } catch { continue; }
                if ((t == SurfaceTypeEnum.kCylinderSurface || t == SurfaceTypeEnum.kConeSurface) && (best < 0 || areas[i] > areas[best])) best = i;
            }
            if (best < 0) return shape;

            Point p0; UnitVector v;
            if (faces[best].Geometry is Cylinder cyl) { p0 = cyl.BasePoint; v = cyl.AxisVector; }
            else { Cone cone = (Cone)faces[best].Geometry; p0 = cone.BasePoint; v = cone.AxisVector; }

            double ok = 0;
            for (int i = 0; i < faces.Count; i++)
            {
                try
                {
                    object g = faces[i].Geometry;
                    bool turns = false;
                    switch (g)
                    {
                        case Plane pl: turns = Math.Abs(Dot(pl.Normal, v)) > 0.999; break;
                        case Cylinder cy: turns = Math.Abs(Dot(cy.AxisVector, v)) > 0.999 && OnAxis(cy.BasePoint, p0, v); break;
                        case Cone co: turns = Math.Abs(Dot(co.AxisVector, v)) > 0.999 && OnAxis(co.BasePoint, p0, v); break;
                        case Torus to: turns = Math.Abs(Dot(to.AxisVector, v)) > 0.999 && OnAxis(to.CenterPoint, p0, v); break;
                        case Sphere sp: turns = OnAxis(sp.CenterPoint, p0, v); break;
                    }
                    if (turns) ok += areas[i];
                }
                catch { }
            }
            shape.Rotational = ok / total > 0.97;
            if (shape.Rotational) shape.RotationAxis = AxisOf(v);
            return shape;
        }

        private static double Dot(UnitVector a, UnitVector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        /// <summary>Is point <paramref name="p"/> on the line through <paramref name="p0"/> along <paramref name="v"/>? (0.1 mm)</summary>
        private static bool OnAxis(Point p, Point p0, UnitVector v)
        {
            double dx = p.X - p0.X, dy = p.Y - p0.Y, dz = p.Z - p0.Z;
            double t = dx * v.X + dy * v.Y + dz * v.Z;
            double ex = dx - t * v.X, ey = dy - t * v.Y, ez = dz - t * v.Z;
            return Math.Sqrt(ex * ex + ey * ey + ez * ez) < 0.01;
        }

        private static (double, double, double) Unit(ModelAxis a) =>
            a == ModelAxis.X ? (1, 0, 0) : a == ModelAxis.Y ? (0, 1, 0) : (0, 0, 1);

        public static ModelAxis AxisOf(UnitVector v)
        {
            if (Math.Abs(v.X) > 0.999) return ModelAxis.X;
            if (Math.Abs(v.Y) > 0.999) return ModelAxis.Y;
            if (Math.Abs(v.Z) > 0.999) return ModelAxis.Z;
            return ModelAxis.None;
        }
    }
}
