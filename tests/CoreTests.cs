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
        var usable = new Region(1.8, 1.8, 55.6, 34.7);
        var obstacles = new List<Region> { new Region(36.8, 0.2, 58.4, 8.8), new Region(36.8, 0.2, 38.9, 11.3), new Region(24.9, 0.2, 36.8, 2.7) };
        var ortho = new ViewGroup { BW = 148, BH = 51.6, BelowH = 51.6, SideW = 51.6 };
        var iso = new ViewGroup { BW = 141, BH = 102, Prefer = Corner.TopRight, Flexible = true, MarginL = 0.5, MarginR = 0.5, MarginT = 0.5, MarginB = 0.5 };
        bool packed = Layout.Pack(new List<ViewGroup> { ortho, iso }, usable, obstacles);
        Check("tank packs", packed);
        Check("tank ortho scale at least 1:8", ortho.Scale >= 1 / 8.0 - 1e-9, "1:" + (1 / ortho.Scale).ToString("0.#"));
        Check("groups do not overlap", !ortho.Box.Overlaps(iso.Box));
        Check("nothing on the title block", obstacles.All(o => !o.Overlaps(ortho.Box) && !o.Overlaps(iso.Box)));
        Check("top view above the base view", ortho.BelowCY > ortho.BaseCY && Math.Abs(ortho.BaseCX - (ortho.Box.X0 + ortho.MarginL + 51.6 * ortho.Scale + ortho.Gap + 148 * ortho.Scale / 2)) < 1e-6);
        Check("side view left of the base view", ortho.SideCX < ortho.BaseCX);
        var flatG = new ViewGroup { BW = 100, BH = 30, EdgeW = 0.2, Prefer = Corner.BottomLeft, Fixed = true, MarginT = 3.2, MarginL = 3.2, MarginB = 2.5, Gap = 4 };
        var isoF = new ViewGroup { BW = 40, BH = 40, Prefer = Corner.TopRight, Flexible = true };
        var orthoF = new ViewGroup { BW = 60, BH = 100, BelowH = 30, SideW = 30 };
        Layout.Pack(new List<ViewGroup> { flatG, isoF, orthoF }, usable, obstacles);
        Check("flat pattern bottom left", Math.Abs(flatG.Box.X0 - usable.X0) < 1e-6 && Math.Abs(flatG.Box.Y0 - usable.Y0) < 1e-6);
        Check("edge view right of the flat pattern", flatG.EdgeCX > flatG.BaseCX);
        Check("inside the sheet", ortho.Box.X0 >= usable.X0 && ortho.Box.Y0 >= usable.Y0 && iso.Box.X1 <= usable.X1 + 1e-6 && iso.Box.Y1 <= usable.Y1 + 1e-6);

        var plate = new ViewGroup { BW = 68, BH = 8, SideW = 1.5, MarginT = 3.2, MarginL = 3.2, MarginB = 2.5, Gap = 4 };
        Layout.Pack(new List<ViewGroup> { plate }, usable, obstacles);
        Check("plate 680x80 gets 1:2 or larger", plate.Scale >= 0.5 - 1e-9, "1:" + (1 / plate.Scale).ToString("0.#"));

        Console.WriteLine(_failed == 0 ? "All passed." : _failed + " failed.");
        return _failed == 0 ? 0 : 1;
    }
}
