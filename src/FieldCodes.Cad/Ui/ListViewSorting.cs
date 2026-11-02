using System;
using System.Collections;
using System.Windows.Forms;
using FieldCodes.Review;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// Makes a details ListView sortable by clicking its column headers: the first
    /// click sorts ascending, the next reverses, and the header shows an arrow.
    /// Numbers sort as numbers and text with numbers in it naturally ("PT 9" before
    /// "PT 10") -- see <see cref="NaturalOrder"/>. Empty and "-" cells always sort
    /// last. Ties fall back to the first column.
    ///
    /// Columns stay resizable (the ListView default). The sorter is only attached
    /// once a column is chosen, so a list keeps the order it was filled in until
    /// someone asks otherwise; fill with Items.AddRange so a sorted list sorts once,
    /// not once per item.
    ///
    /// UNTESTED as UI; the ordering itself is unit tested (NaturalOrder).
    /// </summary>
    internal sealed class ListViewSorting : IComparer
    {
        private const string Up = " \u25B2";
        private const string Down = " \u25BC";

        private readonly ListView _list;
        private int _column = -1;
        private SortOrder _order = SortOrder.None;

        private ListViewSorting(ListView list)
        {
            _list = list;
            _list.ColumnClick += OnColumnClick;
            _list.HandleCreated += (s, e) => Resort();
        }

        /// <summary>Hooks header clicks on <paramref name="list"/>. Call once the
        /// columns exist.</summary>
        public static ListViewSorting Attach(ListView list)
        {
            if (list == null) throw new ArgumentNullException("list");
            return new ListViewSorting(list);
        }

        /// <summary>The sorted column, or -1 while the fill order stands.</summary>
        public int Column { get { return _column; } }

        public SortOrder Order { get { return _order; } }

        /// <summary>Sorts by a column in the given direction, as a header click would.</summary>
        public void SortBy(int column, SortOrder order)
        {
            if (column < 0 || column >= _list.Columns.Count || order == SortOrder.None)
            {
                _column = -1;
                _order = SortOrder.None;
                _list.ListViewItemSorter = null;
                ShowArrow();
                return;
            }

            _column = column;
            _order = order;
            ShowArrow();

            if (_list.ListViewItemSorter == this) _list.Sort();
            else _list.ListViewItemSorter = this;      // assigning sorts
        }

        /// <summary>Sorts again after the list was refilled (AddRange already does).</summary>
        public void Resort()
        {
            if (_column >= 0 && _list.ListViewItemSorter == this && !_list.IsDisposed) _list.Sort();
        }

        private void OnColumnClick(object sender, ColumnClickEventArgs e)
        {
            var order = e.Column == _column && _order == SortOrder.Ascending
                ? SortOrder.Descending
                : SortOrder.Ascending;
            SortBy(e.Column, order);
        }

        public int Compare(object x, object y)
        {
            var a = x as ListViewItem;
            var b = y as ListViewItem;
            if (a == null || b == null || _column < 0) return 0;

            var ta = Cell(a, _column);
            var tb = Cell(b, _column);

            // Missing values last whichever way the column is sorted.
            var pa = NaturalOrder.IsPlaceholder(ta);
            var pb = NaturalOrder.IsPlaceholder(tb);
            if (pa != pb) return pa ? 1 : -1;

            var result = NaturalOrder.Instance.Compare(ta, tb);
            if (result == 0 && _column != 0)
                result = NaturalOrder.Instance.Compare(Cell(a, 0), Cell(b, 0));

            return _order == SortOrder.Descending ? -result : result;
        }

        private static string Cell(ListViewItem item, int column)
        {
            if (column == 0) return item.Text;
            return column < item.SubItems.Count ? item.SubItems[column].Text : string.Empty;
        }

        private void ShowArrow()
        {
            for (var i = 0; i < _list.Columns.Count; i++)
            {
                var header = _list.Columns[i];
                var title = Bare(header.Text);
                header.Text = i == _column
                    ? title + (_order == SortOrder.Descending ? Down : Up)
                    : title;
            }
        }

        private static string Bare(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.EndsWith(Up, StringComparison.Ordinal)) return text.Substring(0, text.Length - Up.Length);
            if (text.EndsWith(Down, StringComparison.Ordinal)) return text.Substring(0, text.Length - Down.Length);
            return text;
        }

        /// <summary>
        /// Keeps the last column filling whatever width the others leave, so a list
        /// uses the space it has instead of scrolling sideways or leaving a gap. The
        /// other columns keep the widths the person dragged them to.
        /// </summary>
        public static void StretchLastColumn(ListView list, int minimumWidth)
        {
            if (list == null) return;

            EventHandler fit = (s, e) =>
            {
                if (list.IsDisposed || list.Columns.Count == 0 || list.View != View.Details) return;
                var used = 0;
                for (var i = 0; i < list.Columns.Count - 1; i++) used += list.Columns[i].Width;
                var room = list.ClientSize.Width - used - 4;
                var last = list.Columns[list.Columns.Count - 1];
                var width = Math.Max(minimumWidth, room);
                if (last.Width != width) last.Width = width;
            };

            list.ClientSizeChanged += fit;
            list.ColumnWidthChanged += (s, e) =>
            {
                // Re-fit only when another column changed, or the last one would
                // fight the person dragging it.
                if (e.ColumnIndex < list.Columns.Count - 1) fit(s, EventArgs.Empty);
            };
        }
    }
}
