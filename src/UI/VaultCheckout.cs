using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AutoDraw.Core;
using Inventor;
using Environment = System.Environment;
using Path = System.IO.Path;

namespace AutoDraw.UI
{
    /// <summary>
    /// Before AutoDraw writes files: finds the ones that are read-only (in Vault: checked in) and asks whether to check
    /// them out. Yes runs Vault's own Check Out (VaultCheckoutTop) on the drawing or each model, where the files are
    /// ticked; the files are looked at again afterwards.
    /// </summary>
    internal static class VaultCheckout
    {
        private const string CheckOutCommand = "VaultCheckoutTop";

        public static bool IsReadOnly(Document d)
        {
            try
            {
                string f = d?.FullFileName;
                return !string.IsNullOrEmpty(f) && System.IO.File.Exists(f) && (System.IO.File.GetAttributes(f) & FileAttributes.ReadOnly) != 0;
            }
            catch { return false; }
        }

        private static string List(List<Document> docs) =>
            string.Join(Environment.NewLine, docs.Take(15).Select(d => "  " + Path.GetFileName(d.FullFileName))) +
            (docs.Count > 15 ? Environment.NewLine + "  … and " + (docs.Count - 15) + " more" : "");

        /// <summary>False when the user cancels. Read-only files that stay read-only are not written (Inventor cannot save them).</summary>
        /// <param name="tops">the documents Vault's Check Out is run on (the drawing, the models)</param>
        public static bool Ensure(Inventor.Application app, IWin32Window owner, IEnumerable<Document> docs, IEnumerable<Document> tops)
        {
            List<Document> readOnly = docs.Where(d => d != null).Distinct().Where(IsReadOnly).ToList();
            if (readOnly.Count == 0) return true;

            ControlDefinition cmd = null;
            try { cmd = app.CommandManager.ControlDefinitions[CheckOutCommand]; } catch { }
            if (cmd == null)
                return MessageBox.Show(owner, readOnly.Count + " file(s) are read-only and cannot be written:" + Environment.NewLine + List(readOnly) +
                    Environment.NewLine + Environment.NewLine + "Go on without them?", "AutoDraw", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK;

            DialogResult r = MessageBox.Show(owner, readOnly.Count + " file(s) are checked in (read-only):" + Environment.NewLine + List(readOnly) +
                Environment.NewLine + Environment.NewLine + "Check them out in Vault now? Yes opens Vault's Check Out; tick the files there." +
                Environment.NewLine + "No goes on without them (they are not written).",
                "AutoDraw", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.No) return true;

            Document active = null;
            try { active = app.ActiveDocument; } catch { }
            foreach (Document top in tops.Where(t => t != null).Distinct())
            {
                if (!readOnly.Any(IsReadOnly)) break;
                try
                {
                    Document shown = top;
                    if (top.Views.Count == 0) shown = app.Documents.Open(top.FullFileName, true);   // opened invisibly: give it a window
                    shown.Activate();
                    cmd.Execute2(true);
                }
                catch (Exception ex) { Log.Write("Vault check out of " + top.DisplayName + " failed: " + ex.Message); }
            }
            try { active?.Activate(); } catch { }

            List<Document> still = readOnly.Where(IsReadOnly).ToList();
            if (still.Count == 0) return true;
            return MessageBox.Show(owner, still.Count + " file(s) are still read-only:" + Environment.NewLine + List(still) +
                Environment.NewLine + Environment.NewLine + "Go on without them?", "AutoDraw", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK;
        }
    }
}
