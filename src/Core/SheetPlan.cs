using System.Collections.Generic;
using System.Linq;

namespace AutoDraw.Core
{
    /// <summary>One sheet of the production drawing.</summary>
    internal sealed class SheetJob
    {
        public DocNode Node;
        public int SheetNo;      // 1-based
        public int Part;         // 0 for the first sheet of a document, 1, 2 … for the extra sheets of a large assembly
        public int Parts;        // sheets this document has
    }

    /// <summary>
    /// The sheet order: main assembly, sub-assemblies, folded sheet metal, flat sheet metal, lathe parts, tubes, general
    /// parts; alphabetical (numbers by value) within each kind. Only ticked documents get sheets.
    /// </summary>
    internal static class SheetPlan
    {
        public static List<SheetJob> Make(IEnumerable<DocNode> nodes, int largeLimit)
        {
            List<DocNode> ticked = nodes.Where(n => n.Checked && !n.IsGroup).ToList();
            var ordered = ticked
                .OrderBy(n => n.IsMain ? 0 : 1)
                .ThenBy(n => (int)n.Kind)
                .ThenBy(n => n.Name, Naming.Natural)
                .ToList();

            var jobs = new List<SheetJob>();
            int no = 1;
            foreach (DocNode n in ordered)
            {
                int count = System.Math.Max(1, n.SheetCount(largeLimit));
                for (int i = 0; i < count; i++)
                    jobs.Add(new SheetJob { Node = n, SheetNo = no++, Part = i, Parts = count });
            }
            return jobs;
        }

        /// <summary>Fills <see cref="DocNode.SheetNo"/> of every node (0 for nodes without a sheet).</summary>
        public static List<SheetJob> Number(IEnumerable<DocNode> nodes, int largeLimit)
        {
            List<DocNode> all = nodes.ToList();
            foreach (DocNode n in all) n.SheetNo = 0;
            List<SheetJob> jobs = Make(all, largeLimit);
            foreach (SheetJob j in jobs) if (j.Part == 0) j.Node.SheetNo = j.SheetNo;
            return jobs;
        }
    }
}
