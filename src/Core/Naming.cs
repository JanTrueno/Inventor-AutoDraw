using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AutoDraw.Core
{
    /// <summary>Order number checks, the drawing's file name and the sheet number text.</summary>
    internal static class Naming
    {
        public static string Digits(string order) => new string((order ?? "").Where(char.IsDigit).ToArray());

        /// <summary>Why the order number cannot be used, or null when it can (same rules as BlomViewer Export).</summary>
        public static string OrderProblem(string order, AppSettings s)
        {
            string d = Digits(order);
            if (d.Length == 0) return "Fill in the order number.";
            if (!string.IsNullOrEmpty(s.OrderPlaceholder) && d == Digits(s.OrderPlaceholder)) return "Order number not filled in (" + s.OrderPlaceholder + ").";
            if (d.Length < s.OrderMinDigits) return "Order number too short.";
            return null;
        }

        /// <summary>The sheet number as the bewerking iProperty holds it: 1 → "001".</summary>
        public static string SheetText(int sheetNo) => sheetNo.ToString("000");

        /// <summary>
        /// &lt;folder&gt;\&lt;order&gt;_&lt;assembly&gt;_&lt;y-M-d&gt;.idw; when that file exists already, _2, _3 … is added.
        /// </summary>
        public static string DrawingPath(string folder, string order, string assemblyName, DateTime date, Func<string, bool> exists = null)
        {
            exists = exists ?? File.Exists;
            string stem = Clean(order.Trim()) + "_" + Clean(assemblyName) + "_" + date.ToString("yyyy-M-d");
            string path = Path.Combine(folder, stem + ".idw");
            for (int i = 2; exists(path); i++) path = Path.Combine(folder, stem + "_" + i + ".idw");
            return path;
        }

        private static string Clean(string s)
        {
            char[] bad = Path.GetInvalidFileNameChars();
            return new string((s ?? "").Select(c => bad.Contains(c) ? '_' : c).ToArray());
        }

        /// <summary>Alphabetical order where numbers compare by value: Z2 before Z10.</summary>
        public static int NaturalCompare(string a, string b)
        {
            a = a ?? ""; b = b ?? "";
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    string na = a.Substring(si, i - si).TrimStart('0'), nb = b.Substring(sj, j - sj).TrimStart('0');
                    if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                    int c = string.CompareOrdinal(na, nb);
                    if (c != 0) return c;
                }
                else
                {
                    int c = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                    if (c != 0) return c;
                    i++; j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }

        public static readonly IComparer<string> Natural = Comparer<string>.Create(NaturalCompare);
    }
}
