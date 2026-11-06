using System;
using System.Collections.Generic;
using System.Linq;
using AutoDraw.Core;
using AutoDraw.Drawing;

// Checks the logic that does not need Inventor: sheet order and numbers, file names, order numbers, scales.
internal static class CoreTests
{
    private static int _failed;

    private static void Check(string what, bool ok, string detail = "")
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what + (string.IsNullOrEmpty(detail) ? "" : "   [" + detail + "]"));
        if (!ok) _failed++;
    }

    private static DocNode Node(string name, DocKind kind, bool main = false, int rows = 0) => new DocNode
    {
        Name = name, IsAssembly = kind == DocKind.Assembly, Detected = kind, IsMain = main, OwnRows = rows, Quantity = 1
    };

    private static int Main()
    {
        // natural order
        var names = new List<string> { "Z10", "z2", "Z1", "P3" };
        names.Sort(Naming.Natural);
        Check("natural sort", string.Join(",", names) == "P3,Z1,z2,Z10", string.Join(",", names));

        // sheet plan: main, sub-assemblies, folded, flat, lathe, tube, general; alphabetical within each
        var main = Node("lekbak_S1", DocKind.Assembly, true, 20);
        var nodes = new List<DocNode>
        {
            main,
            Node("frame_S3", DocKind.Assembly, false, 3),
            Node("frame_S2", DocKind.Assembly, false, 3),
            Node("plate_P2", DocKind.SheetMetalFlat),
            Node("bracket_Z10", DocKind.SheetMetalFolded),
            Node("bracket_Z2", DocKind.SheetMetalFolded),
            Node("shaft_I1", DocKind.Lathe),
            Node("tube_K1", DocKind.Tube),
            Node("misc", DocKind.General),
        };
        nodes.Add(new DocNode { Name = "bolt", Detected = DocKind.General, IsContentCenter = true, Checked = false });
        List<SheetJob> jobs = SheetPlan.Number(nodes, 15);
        string order = string.Join(",", jobs.Select(j => j.Node.Name + (j.Part > 0 ? "#" + j.Part : "")));
        Check("sheet order", order == "lekbak_S1,lekbak_S1#1,frame_S2,frame_S3,bracket_Z2,bracket_Z10,plate_P2,shaft_I1,tube_K1,misc", order);
        Check("large main assembly has 2 sheets", jobs.Count(j => j.Node == main) == 2);
        Check("sheet numbers continue after the 2 main sheets", nodes.First(n => n.Name == "frame_S2").SheetNo == 3);
        Check("unticked node has no sheet", nodes.First(n => n.Name == "bolt").SheetNo == 0);
        nodes.First(n => n.Name == "frame_S2").SheetsOverride = 3;
        jobs = SheetPlan.Number(nodes, 15);
        Check("sheet count override", nodes.First(n => n.Name == "frame_S3").SheetNo == 6, nodes.First(n => n.Name == "frame_S3").SheetNo.ToString());
        nodes.First(n => n.Name == "misc").KindOverride = DocKind.SheetMetalFolded;
        jobs = SheetPlan.Number(nodes, 15);
        Check("kind override moves the sheet", jobs.FindIndex(j => j.Node.Name == "misc") < jobs.FindIndex(j => j.Node.Name == "plate_P2"));

        Check("sheet text", Naming.SheetText(7) == "007" && Naming.SheetText(123) == "123");

        // file name
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\x\160260496_lekbak_S1_2026-11-6.idw" };
        string p = Naming.DrawingPath(@"C:\x", "160260496", "lekbak_S1", new DateTime(2026, 11, 6), existing.Contains);
        Check("file name gets _2 when taken", p == @"C:\x\160260496_lekbak_S1_2026-11-6_2.idw", p);
        p = Naming.DrawingPath(@"C:\x", "160260496", "lekbak_S1", new DateTime(2026, 12, 16), existing.Contains);
        Check("file name", p == @"C:\x\160260496_lekbak_S1_2026-12-16.idw", p);

        // order number
        var s = new AppSettings();
        Check("order ok", Naming.OrderProblem("160260496", s) == null);
        Check("order placeholder refused", Naming.OrderProblem("160100", s) != null);
        Check("order too short refused", Naming.OrderProblem("16025", s) != null);
        Check("order empty refused", Naming.OrderProblem(" ", s) != null);

        // scales
        Check("snap 0.3 -> 1:4", Math.Abs(Scales.Snap(0.3) - 0.25) < 1e-9, Scales.Snap(0.3).ToString());
        Check("snap 1 -> 1:1", Scales.Snap(1.0) == 1);
        Check("snap tiny -> 1:200", Math.Abs(Scales.Snap(0.0001) - 1 / 200.0) < 1e-12);
        Check("below 1:5 is 1:6", Math.Abs(Scales.Below(0.2) - 1 / 6.0) < 1e-9);
        Check("fit", Math.Abs(Scales.Fit(100, 50, 50, 50, 1) - 0.5) < 1e-9);

        // layout: the mixing tank on an A2 sheet (usable area and title block as on the company sheet)
        var usable = new Region(1.8, 1.8, 57.6, 40.2);
        var obstacles = new List<Region> { new Region(36.8, 0.2, 58.4, 8.8), new Region(36.8, 0.2, 38.9, 11.3), new Region(0, 0.2, 36.8, 2.7),
            new Region(0, 32.8, 14.9, 41), new Region(54.6, 37, 58.4, 41), new Region(56.7, 31.1, 58.4, 41) };
        var ortho = new ViewGroup { BW = 148, BH = 51.6, BelowH = 51.6, SideW = 51.6 };
        var iso = new ViewGroup { BW = 141, BH = 102, Prefer = Corner.TopRight, Flexible = true, MarginL = 0.5, MarginR = 0.5, MarginT = 0.5, MarginB = 0.5 };
        bool packed = Layout.Pack(new List<ViewGroup> { ortho, iso }, usable, obstacles);
        Check("tank packs", packed);
        Check("header box stays free", ortho.Parts().All(q => !q.R.Overlaps(obstacles[3])) && !iso.Box.Overlaps(obstacles[3]));
        Check("tank ortho scale at least 1:8", ortho.Scale >= 1 / 8.0 - 1e-9, "1:" + (1 / ortho.Scale).ToString("0.#"));
        Check("groups do not overlap", !ortho.Box.Overlaps(iso.Box));
        Check("tank views spread and inside", ortho.ExtraX > 0 && ortho.Box.X0 >= usable.X0 - 1e-6 && ortho.Box.X1 <= usable.X1 + 1e-6, ortho.ExtraX.ToString("0.0"));
        Check("nothing on the title block", obstacles.All(o => ortho.Parts().All(q => !q.R.Overlaps(o)) && !o.Overlaps(iso.Box)));
        Check("top view above the base view", ortho.BelowCY > ortho.BaseCY);
        Check("side view left of the base view", ortho.SideCX < ortho.BaseCX);

        // mixing_tank_Z2: folded part 457 x 860 (top view 217 deep), flat pattern 1007 x 457, iso 325 x 886
        var z2Flat = new ViewGroup { BW = 100.7, BH = 45.7, EdgeW = 0.2, SideLeft = false, Prefer = Corner.BottomLeft, Fixed = true, MarginT = 3.2, MarginL = 3.2, MarginB = 2.5, Gap = 4 };
        var z2Iso = new ViewGroup { BW = 32.5, BH = 88.6, Prefer = Corner.TopRight, Flexible = true, MarginL = 0.5, MarginR = 0.5, MarginT = 0.5, MarginB = 0.5 };
        var z2Ortho = new ViewGroup { BW = 45.7, BH = 86, BelowH = 21.7, SideW = 21.7 };
        bool z2 = Layout.Pack(new List<ViewGroup> { z2Flat, z2Ortho, z2Iso }, usable, obstacles);
        Check("Z2 packs at 1:5 or larger", z2 && z2Ortho.Scale >= 0.2 - 1e-9, "1:" + (1 / z2Ortho.Scale).ToString("0.#"));
        Check("Z2 flat pattern bottom left", Math.Abs(z2Flat.Box.X0 - usable.X0) < 1e-6 && z2Flat.Box.Y0 < 5, z2Flat.Box.Y0.ToString("0.0"));
        Check("Z2 groups apart", !z2Flat.Box.Overlaps(z2Ortho.Box) && !z2Iso.Box.Overlaps(z2Ortho.Box) && !z2Iso.Box.Overlaps(z2Flat.Box));
        Check("Z2 views spread out", z2Ortho.ExtraX > 0 || z2Ortho.ExtraY > 0, z2Ortho.ExtraX.ToString("0.0") + "/" + z2Ortho.ExtraY.ToString("0.0"));
        Check("Z2 nothing on obstacles", obstacles.All(o => !o.Overlaps(z2Flat.Box) && !o.Overlaps(z2Ortho.Box) && !o.Overlaps(z2Iso.Box)));
        var flatG = new ViewGroup { BW = 100, BH = 30, EdgeW = 0.2, SideLeft = false, Prefer = Corner.BottomLeft, Fixed = true, MarginT = 3.2, MarginL = 3.2, MarginB = 2.5, Gap = 4 };
        var isoF = new ViewGroup { BW = 40, BH = 40, Prefer = Corner.TopRight, Flexible = true };
        var orthoF = new ViewGroup { BW = 60, BH = 100, BelowH = 30, SideW = 30 };
        Layout.Pack(new List<ViewGroup> { flatG, isoF, orthoF }, usable, obstacles);
        Check("flat pattern bottom left", Math.Abs(flatG.Box.X0 - usable.X0) < 1e-6 && flatG.Box.Y0 < 5);
        Check("edge view right of the flat pattern", flatG.EdgeCX > flatG.BaseCX);
        Check("inside the sheet", ortho.Box.X0 >= usable.X0 && ortho.Box.Y0 >= usable.Y0 && iso.Box.X1 <= usable.X1 + 1e-6 && iso.Box.Y1 <= usable.Y1 + 1e-6);

        var plate = new ViewGroup { BW = 68, BH = 8, SideW = 1.5, MarginT = 3.2, MarginL = 3.2, MarginB = 2.5, Gap = 4 };
        Layout.Pack(new List<ViewGroup> { plate }, usable, obstacles);
        Check("plate 680x80 gets 1:2 or larger", plate.Scale >= 0.5 - 1e-9, "1:" + (1 / plate.Scale).ToString("0.#"));

        // parts: flat pattern bottom left, views above it (top view at the top), iso in the right-hand column
        Func<Region, string> B = r => $"({r.X0:0.0},{r.Y0:0.0})-({r.X1:0.0},{r.Y1:0.0})";
        foreach (var (name, fw, fh, bw, bh, d, iw, ih) in new[] { ("Z3", 100.7, 45.7, 45.7, 86.0, 21.7, 32.5, 88.6), ("Z6", 55.6, 55.6, 45.0, 4.0, 45.0, 63.0, 42.0) })
        {
            var pf = new ViewGroup { BW = fw, BH = fh, EdgeW = 0.2, SideLeft = false, MarginT = 3.2, MarginL = 3.2, MarginB = 2.5, Gap = 4 };
            var po = new ViewGroup { BW = bw, BH = bh, BelowH = d, SideW = d };
            var pi = new ViewGroup { BW = iw, BH = ih, Flexible = true, MarginL = 0.5, MarginR = 0.5, MarginT = 0.5, MarginB = 0.5 };
            Layout.PartZones(usable, 36.8, pf, po, pi);
            bool ok = Layout.Pack(new List<ViewGroup> { pf, po, pi }, usable, obstacles);
            string where = "views " + B(po.Box) + " flat " + B(pf.Box) + " iso " + B(pi.Box);
            Check(name + " packs", ok, "1:" + (1 / po.Scale).ToString("0.#") + ", iso 1:" + (1 / pi.Scale).ToString("0.#"));
            Check(name + " flat pattern bottom left", Math.Abs(pf.Box.X0 - usable.X0) < 1e-6 && pf.Box.Y0 < 5, where);
            Check(name + " views above the flat pattern, left of the title block column", po.Box.Y0 >= pf.Box.Y1 - 1e-6 && po.Box.X1 <= 35.8 + 1e-6, where);
            Check(name + " top view at the top", po.Box.Y1 > usable.Y1 - 1.5, where);
            Check(name + " iso in the right-hand column", pi.Box.X0 >= 36.8 - 1e-6, where);
            Check(name + " nothing on obstacles", obstacles.All(o => pf.Parts().Concat(po.Parts()).Concat(pi.Parts()).All(q => !q.R.Overlaps(o))), where);
        }

        // assembly: front and side view low, the top view high beside the header box
        var asmG = new ViewGroup { BW = 51.6, BH = 148, BelowH = 51.6, SideW = 51.6, FillY = true, Area = new Region(1.8, 1.8, 36.3, 40.2) };
        Layout.Pack(new List<ViewGroup> { asmG }, usable, obstacles);
        Check("assembly top view rises beside the header", asmG.Box.Y1 > 33 && asmG.Box.Y0 < 4, B(asmG.Box) + " 1:" + (1 / asmG.Scale).ToString("0.#"));
        Check("assembly views clear of the header", asmG.Parts().All(q => !q.R.Overlaps(obstacles[3])));

        Console.WriteLine(_failed == 0 ? "All passed." : _failed + " failed.");
        return _failed == 0 ? 0 : 1;
    }
}
