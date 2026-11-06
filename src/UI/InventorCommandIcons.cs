using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using Inventor;
using AutoDraw.Core;

namespace AutoDraw.UI
{
    /// <summary>
    /// The icons of Inventor's own commands (Extrude, Face, Corner Round...), read at run time through the command's
    /// ControlDefinition. Nothing is copied: the pictures come from the running Inventor and match its current theme.
    /// Every lookup can fail (null), in which case the caller draws its own fallback icon.
    /// </summary>
    internal static class InventorCommandIcons
    {
        private static Inventor.Application _app;
        private static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();
        private static Dictionary<string, ControlDefinition> _ribbonDefinitions;

        public static void Init(Inventor.Application app)
        {
            _app = app;
            _ribbonDefinitions = null;
            lock (Cache) Cache.Clear();
        }

        /// <summary>The command's icon as a bitmap: the 32 px one when <paramref name="large"/>, otherwise the 16 px one.</summary>
        public static Bitmap Get(string command, bool large)
        {
            string key = command + (large ? "|32" : "|16");
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out Bitmap cached)) return cached;
                Bitmap bmp = null;
                try
                {
                    object pic = Picture(command, large);
                    if (pic != null) bmp = ToBitmap(pic);
                }
                catch (Exception ex) { Log.Write("Command icon " + command + ": " + ex.Message); }
                Cache[key] = bmp;
                return bmp;
            }
        }

        /// <summary>The raw IPictureDisp of a command, e.g. to hand to AddButtonDefinition; null if unavailable.</summary>
        public static object Picture(string command, bool large)
        {
            if (_app == null) return null;
            string property = large ? "LargeIcon" : "StandardIcon";

            ControlDefinition def = null;
            try { def = _app.CommandManager.ControlDefinitions[command]; } catch { /* not found by name */ }
            object pic = Read(def, property);
            if (pic != null) return pic;

            // some definitions only expose their icon once a ribbon has loaded them
            def = FindOnRibbons(command);
            return Read(def, property);
        }

        private static object Read(ControlDefinition def, string property)
        {
            if (def == null) return null;
            try { return def.GetType().InvokeMember(property, System.Reflection.BindingFlags.GetProperty, null, def, null); }
            catch { return null; }
        }

        private static ControlDefinition FindOnRibbons(string command)
        {
            if (_ribbonDefinitions == null)
            {
                _ribbonDefinitions = new Dictionary<string, ControlDefinition>();
                try
                {
                    foreach (Ribbon ribbon in _app.UserInterfaceManager.Ribbons)
                        foreach (RibbonTab tab in ribbon.RibbonTabs)
                            foreach (RibbonPanel panel in tab.RibbonPanels)
                                foreach (CommandControl cc in panel.CommandControls)
                                {
                                    try { ControlDefinition d = cc.ControlDefinition; _ribbonDefinitions[d.InternalName] = d; }
                                    catch { /* separator or control without a definition */ }
                                }
                }
                catch { /* ribbons not available yet */ }
            }
            return _ribbonDefinitions.TryGetValue(command, out ControlDefinition found) ? found : null;
        }

        // ----- IPictureDisp to bitmap -----------------------------------------------------------------------------

        [ComImport, Guid("7BF80980-BF32-101A-8BBB-00AA00300CAB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPicture
        {
            [PreserveSig] int get_Handle(out uint handle);
            [PreserveSig] int get_hPal(out uint handle);
            [PreserveSig] int get_Type(out short type);
        }

        /// <summary>Converts an IPictureDisp (icon or bitmap type) to a bitmap, keeping an icon's alpha channel.</summary>
        public static Bitmap ToBitmap(object picture)
        {
            var p = (IPicture)picture;
            p.get_Handle(out uint raw);                       // OLE_HANDLE is an unsigned 32-bit value
            p.get_Type(out short type);                       // 1 = bitmap, 3 = icon
            var handle = new IntPtr(unchecked((int)raw));
            return type == 3 ? Icon.FromHandle(handle).ToBitmap() : System.Drawing.Image.FromHbitmap(handle);
        }
    }
}
