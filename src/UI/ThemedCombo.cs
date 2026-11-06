using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoDraw.UI
{
    /// <summary>
    /// Drop-down list in the same flat look as <see cref="FieldBox"/>: field colour, no 3D border, an underline once it
    /// holds a value. A dimmed hint item (e.g. "(multiple values)") can stand in for "no single choice".
    /// </summary>
    internal sealed class ThemedCombo : ComboBox
    {
        private readonly Theme _theme;
        private string _hint;

        public ThemedCombo(Theme theme)
        {
            _theme = theme;
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            BackColor = theme.Field;
            ForeColor = theme.Text;
            ItemHeight = 18;
            MaxDropDownItems = 20;
        }

        /// <summary>Fills the list; <paramref name="selected"/> null shows the hint (when one is given) as the choice.</summary>
        public void SetItems(System.Collections.Generic.IEnumerable<string> items, string selected, string hint)
        {
            BeginUpdate();
            Items.Clear();
            _hint = hint;
            if (hint != null) Items.Add(hint);
            foreach (string s in items) Items.Add(s);
            if (selected != null && !Items.Contains(selected)) Items.Add(selected);
            SelectedItem = selected ?? hint;
            EndUpdate();
        }

        /// <summary>The chosen value, or null while the hint is chosen.</summary>
        public string Value => SelectedItem is string s && s != _hint ? s : null;

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var fill = new SolidBrush(selected ? _theme.AccentFill : _theme.Field)) e.Graphics.FillRectangle(fill, e.Bounds);
            string text = Items[e.Index] as string ?? "";
            Color ink = text == _hint ? _theme.Dim : _theme.Text;
            TextRenderer.DrawText(e.Graphics, text, Font, new Rectangle(e.Bounds.X + 3, e.Bounds.Y, e.Bounds.Width - 3, e.Bounds.Height), ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
