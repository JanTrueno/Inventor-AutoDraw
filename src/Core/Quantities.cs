using System.Collections.Generic;
using System.Linq;

namespace AutoDraw.Core
{
    /// <summary>
    /// Total quantities from the structure: a document's total is, for every assembly it is used in, the number of
    /// times it is used there (<see cref="DocNode.UsedIn"/>) times that assembly's quantity. An Aantal typed on an
    /// assembly is its quantity, so everything below it follows (main assembly 3 → every part ×3); typed on a part it
    /// is only that part's value.
    /// </summary>
    internal static class Quantities
    {
        public static void Compute(IEnumerable<DocNode> nodes)
        {
            var memo = new Dictionary<DocNode, int>();

            int Typed(DocNode n, int computed) =>
                n.QuantityText != null && int.TryParse(n.QuantityText.Trim(), out int v) && v >= 0 ? v : computed;

            int Total(DocNode n)
            {
                if (memo.TryGetValue(n, out int t)) return t;
                memo[n] = 0;   // guards against a loop
                t = n.UsedIn.Count == 0 ? 1 : n.UsedIn.Sum(kv => kv.Value * Typed(kv.Key, Total(kv.Key)));
                memo[n] = t;
                return t;
            }

            foreach (DocNode n in nodes)
                if (!n.IsGroup) n.Quantity = Total(n);
        }
    }
}
