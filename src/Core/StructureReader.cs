using System;
using System.Collections.Generic;
using System.Linq;
using Inventor;

namespace AutoDraw.Core
{
    /// <summary>
    /// Reads the main assembly into a tree of unique documents with their total quantities. Suppressed and reference
    /// occurrences are skipped; the children of phantom assemblies count as children of the assembly above them.
    /// Content Center parts are collected in a separate group at the bottom, unticked and closed.
    /// </summary>
    internal sealed class StructureReader
    {
        private readonly Dictionary<string, DocNode> _nodes = new Dictionary<string, DocNode>(StringComparer.OrdinalIgnoreCase);
        private readonly Action<string> _progress;

        public DocNode Root { get; private set; }
        public DocNode ContentGroup { get; private set; }
        public IEnumerable<DocNode> AllDocs => _nodes.Values;

        public StructureReader(Action<string> progress) { _progress = progress; }

        public void Read(AssemblyDocument main)
        {
            Root = NewNode((Document)main);
            Root.IsMain = true;
            Root.Quantity = 1;
            ContentGroup = new DocNode { IsGroup = true, Name = "Content Center", Checked = false, Expanded = false };

            Walk(main.ComponentDefinition, Root, 1, RowsOf(Root));

            foreach (DocNode n in _nodes.Values.Where(n => !n.IsMain))
            {
                n.Checked = !n.IsContentCenter;
                n.Expanded = true;
            }
            SortChildren(Root);
            ContentGroup.Children.Sort((a, b) => Naming.NaturalCompare(a.Name, b.Name));
            if (ContentGroup.Children.Count > 0)
            {
                ContentGroup.Parent = Root;
                Root.Children.Add(ContentGroup);
            }
        }

        /// <param name="rows">the documents counted as parts list rows of <paramref name="owner"/> so far</param>
        private void Walk(AssemblyComponentDefinition def, DocNode owner, int multiplier, HashSet<string> rows)
        {
            foreach (ComponentOccurrence occ in def.Occurrences)
            {
                try
                {
                    if (occ.Suppressed) continue;
                    BOMStructureEnum bom = BomOf(occ);
                    if (bom == BOMStructureEnum.kReferenceBOMStructure) continue;

                    Document doc = occ.ReferencedDocumentDescriptor?.ReferencedDocument as Document;
                    if (doc == null) continue;   // virtual component

                    if (bom == BOMStructureEnum.kPhantomBOMStructure)
                    {
                        if (doc is AssemblyDocument phantom) Walk(phantom.ComponentDefinition, owner, multiplier, rows);
                        continue;
                    }

                    string key = doc.FullDocumentName ?? "";
                    if (!_nodes.TryGetValue(key, out DocNode node))
                    {
                        node = NewNode(doc);
                        if (node.IsContentCenter) { node.Parent = ContentGroup; ContentGroup.Children.Add(node); }
                        else { node.Parent = owner; owner.Children.Add(node); }
                        if (_nodes.Count % 10 == 0) _progress?.Invoke("Reading " + _nodes.Count + " documents…");
                    }
                    node.Quantity += multiplier;
                    if (rows.Add(key)) owner.OwnRows = rows.Count;

                    if (doc is AssemblyDocument sub)
                        Walk(sub.ComponentDefinition, node, multiplier, RowsOf(node));
                }
                catch (Exception ex) { Log.Write("Reading occurrence failed: " + ex.Message); }
            }
        }

        private readonly Dictionary<DocNode, HashSet<string>> _rows = new Dictionary<DocNode, HashSet<string>>();

        private HashSet<string> RowsOf(DocNode n)
        {
            if (!_rows.TryGetValue(n, out HashSet<string> set)) _rows[n] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return set;
        }

        private static BOMStructureEnum BomOf(ComponentOccurrence occ)
        {
            try { return occ.BOMStructure; } catch { return BOMStructureEnum.kDefaultBOMStructure; }
        }

        private DocNode NewNode(Document doc)
        {
            var n = new DocNode
            {
                Doc = doc,
                Key = doc.FullDocumentName ?? "",
                Name = System.IO.Path.GetFileNameWithoutExtension(doc.DisplayName ?? doc.FullFileName ?? "?"),
                IsAssembly = doc is AssemblyDocument
            };
            if (string.IsNullOrEmpty(n.Name)) n.Name = doc.DisplayName ?? "?";
            if (doc is PartDocument part)
            {
                try { n.IsContentCenter = part.ComponentDefinition.IsContentMember; } catch { }
            }
            n.Material = n.IsAssembly ? "" : Props.Material(doc);
            if (!n.IsAssembly && !n.IsContentCenter)
            {
                try { n.Detected = KindDetector.Detect(doc); }
                catch (Exception ex) { Log.Write("Detecting " + n.Name + " failed: " + ex.Message); }
            }
            n.Detected = n.IsAssembly ? DocKind.Assembly : n.Detected;
            _nodes[n.Key] = n;
            return n;
        }

        /// <summary>Sub-assemblies first, then folded, flat, lathe, tube, general; by part number within each kind.</summary>
        public static void SortChildren(DocNode n)
        {
            n.Children.Sort((a, b) =>
            {
                if (a.IsGroup != b.IsGroup) return a.IsGroup ? 1 : -1;
                int k = ((int)a.Kind).CompareTo((int)b.Kind);
                if (k != 0) return k;
                int p = Naming.NaturalCompare(a.SortName, b.SortName);
                return p != 0 ? p : Naming.NaturalCompare(a.Name, b.Name);
            });
            foreach (DocNode c in n.Children) SortChildren(c);
        }
    }
}
