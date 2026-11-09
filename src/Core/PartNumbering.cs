using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoDraw.Core
{
    /// <summary>The new number of one document.</summary>
    internal sealed class NumberRow
    {
        public DocNode Node;
        public int Asm;              // assembly code 000–999
        public int Seq;              // 000 = the assembly itself
        public string Value = "";
        public string Current = "";
        public bool Kept;            // the current number fitted the pattern and was kept
        public bool Changed => !string.Equals(Value, Current, StringComparison.Ordinal);
    }

    /// <summary>
    /// Bulk part numbers from the assembly tree, e.g. 1777.110.002-D: the last digits of the order number, the
    /// assembly code, a sequence number within that assembly and a type letter.
    /// <para>Assembly codes: the main assembly is 000, its sub-assemblies 100 … 900, their sub-assemblies 110 … 190,
    /// the next level 111 … 119. Deeper assemblies (or a tenth one on a level) have no code of their own: they and
    /// their contents are numbered in the assembly above them. Sequence 000 is the assembly itself; its parts are
    /// 001, 002 … in tree order. A part used in several assemblies is numbered in the first one (as the tree shows it).</para>
    /// </summary>
    internal static class PartNumbering
    {
        private static readonly Regex TokenRx = new Regex(@"\{(order|asm|seq|type|name)\}", RegexOptions.IgnoreCase);

        public static string DefaultLetter(DocKind k)
        {
            switch (k)
            {
                case DocKind.Assembly: return "S";
                case DocKind.SheetMetalFolded: return "Z";
                case DocKind.SheetMetalFlat: return "P";
                case DocKind.Lathe: return "D";
                case DocKind.Tube: return "B";
                default: return "G";
            }
        }

        public static string Letter(AppSettings s, DocKind k)
        {
            if (s.TypeLetters != null && s.TypeLetters.TryGetValue(k.ToString(), out string v) && v != null) return v.Trim();
            return DefaultLetter(k);
        }

        /// <summary>The last <paramref name="digits"/> digits of the order number (all of them when 0 or shorter).</summary>
        public static string OrderPart(string order, int digits)
        {
            string d = Naming.Digits(order);
            return digits > 0 && d.Length > digits ? d.Substring(d.Length - digits) : d;
        }

        public static string Format(string pattern, string order, int asm, int seq, string type, string name) =>
            TokenRx.Replace(pattern ?? "", m =>
            {
                switch (m.Groups[1].Value.ToLowerInvariant())
                {
                    case "order": return order ?? "";
                    case "asm": return asm.ToString("000");
                    case "seq": return seq.ToString("000");
                    case "type": return type ?? "";
                    default: return name ?? "";
                }
            });

        /// <summary>A regular expression that reads a number made with <paramref name="pattern"/> back.</summary>
        public static Regex Parser(string pattern)
        {
            pattern = pattern ?? "";
            var sb = new StringBuilder("^");
            int i = 0;
            foreach (Match m in TokenRx.Matches(pattern))
            {
                sb.Append(Regex.Escape(pattern.Substring(i, m.Index - i)));
                switch (m.Groups[1].Value.ToLowerInvariant())
                {
                    case "order": sb.Append(@"(?<order>\d+)"); break;
                    case "asm": sb.Append(@"(?<asm>\d{3})"); break;
                    case "seq": sb.Append(@"(?<seq>\d{3,})"); break;
                    case "type": sb.Append(@"(?<type>[A-Za-z0-9]*)"); break;
                    default: sb.Append(@".*?"); break;
                }
                i = m.Index + m.Length;
            }
            sb.Append(Regex.Escape(pattern.Substring(i))).Append('$');
            return new Regex(sb.ToString(), RegexOptions.IgnoreCase);
        }

        /// <summary>True when <paramref name="value"/> is a number of this pattern and this order.</summary>
        public static bool IsOurs(string value, string order, AppSettings s)
        {
            Match m = Parser(s.NumberPattern).Match((value ?? "").Trim());
            return m.Success && m.Groups["seq"].Success &&
                (!m.Groups["order"].Success || m.Groups["order"].Value == OrderPart(order, s.NumberOrderDigits));
        }

        /// <summary>What a pattern is missing, or null when it can be used.</summary>
        public static string PatternProblem(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return "Fill in a pattern.";
            if (pattern.IndexOf("{seq}", StringComparison.OrdinalIgnoreCase) < 0) return "The pattern needs {seq}.";
            if (pattern.IndexOf("{asm}", StringComparison.OrdinalIgnoreCase) < 0) return "The pattern needs {asm}.";
            return null;
        }

        /// <param name="current">the document's present number (the first target iProperty)</param>
        /// <param name="include">false for documents that keep their number untouched (they get no row)</param>
        public static List<NumberRow> Plan(DocNode root, string order, AppSettings s, Func<DocNode, string> current,
            Func<DocNode, bool> include, List<string> warnings)
        {
            string ord = OrderPart(order, s.NumberOrderDigits);
            Regex rx = Parser(s.NumberPattern);
            include = include ?? (n => true);

            // the present number, when it fits the pattern and this order
            var parsed = new Dictionary<DocNode, (int asm, int seq)?>();
            (int asm, int seq)? Existing(DocNode n)
            {
                if (!s.NumberKeepExisting) return null;
                if (parsed.TryGetValue(n, out var p)) return p;
                p = null;
                Match m = rx.Match((current(n) ?? "").Trim());
                if (m.Success && (!m.Groups["order"].Success || m.Groups["order"].Value == ord) &&
                    m.Groups["asm"].Success && m.Groups["seq"].Success &&
                    int.TryParse(m.Groups["asm"].Value, out int a) && int.TryParse(m.Groups["seq"].Value, out int q))
                    p = (a, q);
                parsed[n] = p;
                return p;
            }

            bool Real(DocNode n) => !n.IsGroup && !n.IsContentCenter;

            var groups = new SortedDictionary<int, List<DocNode>>();
            var own = new Dictionary<DocNode, int>();          // assemblies with a code of their own
            var keptCode = new HashSet<DocNode>();
            void Add(int code, DocNode n)
            {
                if (!own.ContainsKey(n) && !include(n)) return;
                if (!groups.TryGetValue(code, out List<DocNode> list)) groups[code] = list = new List<DocNode>();
                list.Add(n);
            }
            void Flatten(int code, DocNode n)
            {
                Add(code, n);
                if (n.IsAssembly) foreach (DocNode c in n.Children.Where(Real)) Flatten(code, c);
            }
            void Structure(DocNode asm, int code, int depth)
            {
                int step = depth == 0 ? 100 : depth == 1 ? 10 : depth == 2 ? 1 : 0;
                var codes = new Dictionary<DocNode, int>();
                List<DocNode> subs = asm.Children.Where(c => Real(c) && c.IsAssembly).ToList();
                if (step > 0)
                {
                    var taken = new HashSet<int>();
                    foreach (DocNode sub in subs)
                    {
                        var ex = Existing(sub);
                        if (ex == null || ex.Value.seq != 0) continue;
                        int k = ex.Value.asm - code;
                        if (k >= step && k <= 9 * step && k % step == 0 && taken.Add(ex.Value.asm)) { codes[sub] = ex.Value.asm; keptCode.Add(sub); }
                    }
                    int next = 1;
                    foreach (DocNode sub in subs.Where(x => !codes.ContainsKey(x)))
                    {
                        while (next <= 9 && taken.Contains(code + next * step)) next++;
                        if (next > 9) { warnings?.Add(sub.Name + ": more than 9 assemblies in " + asm.Name + "; numbered in " + code.ToString("000")); continue; }
                        codes[sub] = code + next * step;
                        taken.Add(code + next * step);
                    }
                }
                else if (subs.Count > 0)
                    warnings?.Add(asm.Name + ": assemblies more than 3 levels deep are numbered in " + code.ToString("000"));

                foreach (DocNode child in asm.Children.Where(Real))
                {
                    if (child.IsAssembly && codes.TryGetValue(child, out int c2))
                    {
                        own[child] = c2;
                        Add(c2, child);
                        Structure(child, c2, depth + 1);
                    }
                    else Flatten(code, child);
                }
            }

            own[root] = 0;
            Add(0, root);
            Structure(root, 0, 0);

            var rows = new List<NumberRow>();
            bool big = false;
            foreach (KeyValuePair<int, List<DocNode>> g in groups)
            {
                int code = g.Key;
                var used = new HashSet<int>();
                var seq = new Dictionary<DocNode, int>();
                var kept = new HashSet<DocNode>();
                foreach (DocNode n in g.Value.Where(own.ContainsKey)) { seq[n] = 0; used.Add(0); if (keptCode.Contains(n) || n == root) kept.Add(n); }
                foreach (DocNode n in g.Value.Where(x => !seq.ContainsKey(x)))
                {
                    var ex = Existing(n);
                    if (ex != null && ex.Value.asm == code && ex.Value.seq >= 1 && used.Add(ex.Value.seq)) { seq[n] = ex.Value.seq; kept.Add(n); }
                }
                int next = 1;
                foreach (DocNode n in g.Value.Where(x => !seq.ContainsKey(x)))
                {
                    while (used.Contains(next)) next++;
                    seq[n] = next;
                    used.Add(next);
                }
                foreach (DocNode n in g.Value)
                {
                    if (!include(n)) continue;
                    if (seq[n] > 999) big = true;
                    string value = Format(s.NumberPattern, ord, code, seq[n], Letter(s, n.Kind), n.Name);
                    string cur = current(n) ?? "";
                    rows.Add(new NumberRow
                    {
                        Node = n, Asm = code, Seq = seq[n], Value = value, Current = cur,
                        Kept = kept.Contains(n) && s.NumberKeepExisting && Existing(n) != null
                    });
                }
            }
            if (big) warnings?.Add("More than 999 documents in one assembly.");
            foreach (var dup in rows.GroupBy(r => r.Value, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
                warnings?.Add("Same number twice: " + dup.Key);
            return rows;
        }
    }
}
