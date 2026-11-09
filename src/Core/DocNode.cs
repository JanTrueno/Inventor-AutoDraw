using System;
using System.Collections.Generic;
using Inventor;

namespace AutoDraw.Core
{
    /// <summary>What kind of drawing sheet a document gets. The order is the sheet order of the production drawing.</summary>
    internal enum DocKind
    {
        Assembly = 0,
        SheetMetalFolded = 1,
        SheetMetalFlat = 2,
        Lathe = 3,
        Tube = 4,
        General = 5
    }

    internal static class DocKinds
    {
        public static readonly DocKind[] All =
            { DocKind.Assembly, DocKind.SheetMetalFolded, DocKind.SheetMetalFlat, DocKind.Lathe, DocKind.Tube, DocKind.General };

        public static readonly DocKind[] PartKinds =
            { DocKind.SheetMetalFolded, DocKind.SheetMetalFlat, DocKind.Lathe, DocKind.Tube, DocKind.General };

        public static string Label(DocKind k)
        {
            switch (k)
            {
                case DocKind.Assembly: return "Assembly";
                case DocKind.SheetMetalFolded: return "Sheet metal folded";
                case DocKind.SheetMetalFlat: return "Sheet metal flat";
                case DocKind.Lathe: return "Lathe part";
                case DocKind.Tube: return "Tube / profile";
                default: return "General";
            }
        }

        public static DocKind? FromLabel(string label)
        {
            foreach (DocKind k in All) if (Label(k) == label) return k;
            return null;
        }
    }

    /// <summary>
    /// One unique document of the main assembly. A file used in several places appears once in the tree, under the
    /// first assembly it was found in; its quantity counts every place.
    /// </summary>
    internal sealed class DocNode
    {
        public Document Doc;
        public string Key = "";          // full document name; the same file gives the same key
        public string Name = "";         // display name without extension
        public bool IsAssembly;
        public bool IsContentCenter;
        public bool IsGroup;             // the "Content Center" group row, not a document
        public bool IsMain;              // the main assembly

        public DocKind Detected = DocKind.General;
        public DocKind? KindOverride;
        public DocKind Kind => IsAssembly ? DocKind.Assembly : (KindOverride ?? Detected);

        /// <summary>Total quantity in the main assembly (nested quantities multiplied).</summary>
        public int Quantity;
        /// <summary>The Aantal value typed in the tree, or null to use <see cref="Quantity"/>.</summary>
        public string QuantityText;
        public string QuantityValue => QuantityText ?? Quantity.ToString();

        /// <summary>Distinct documents directly in this assembly (its own parts list rows).</summary>
        public int OwnRows;
        public int? SheetsOverride;
        public int SheetCount(int largeLimit) => !IsAssembly ? 1 : SheetsOverride ?? (OwnRows > largeLimit ? 2 : 1);

        public bool Checked = true;
        public bool Expanded = true;
        public DocNode Parent;
        public readonly List<DocNode> Children = new List<DocNode>();
        public int Level => Parent == null ? 0 : Parent.Level + 1;

        public string Material = "";
        /// <summary>The part number (the first numbering iProperty); sheets and tree rows sort by it within each kind.</summary>
        public string PartNumber = "";
        public string SortName => string.IsNullOrWhiteSpace(PartNumber) ? Name : PartNumber;
        /// <summary>iProperty values typed in the extra columns; written when the drawing is generated.</summary>
        public readonly Dictionary<string, string> Edited = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public int SheetNo;              // first sheet of this document in the drawing, 0 = no sheet
        public string Status = "";
        public bool StatusBad;

        public IEnumerable<DocNode> Descendants()
        {
            foreach (DocNode c in Children)
            {
                yield return c;
                foreach (DocNode d in c.Descendants()) yield return d;
            }
        }
    }
}
