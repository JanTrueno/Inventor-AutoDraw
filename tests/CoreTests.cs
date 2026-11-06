using System;
using System.Collections.Generic;
using System.Linq;
using AutoDraw.Core;

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

        Console.WriteLine(_failed == 0 ? "All passed." : _failed + " failed.");
        return _failed == 0 ? 0 : 1;
    }
}
