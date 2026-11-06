using System;
using System.Windows.Forms;

namespace AutoDraw.UI
{
    /// <summary>Lets message boxes be owned by Inventor's main window so they appear on top of it.</summary>
    internal sealed class InventorWindow : IWin32Window
    {
        public IntPtr Handle { get; }

        public InventorWindow(int hwnd) { Handle = new IntPtr(hwnd); }
    }
}
